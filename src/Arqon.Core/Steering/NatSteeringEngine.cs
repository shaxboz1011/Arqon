using System.Collections.Concurrent;
using System.Net;
using Arqon.Core.Links;

namespace Arqon.Core.Steering;

public sealed record ProcessInfo(int Pid, string ExeName);

/// <summary>Finds which process owns a local TCP/UDP port. Returns null when the owner is unknown.</summary>
public interface IProcessResolver
{
    ProcessInfo? Resolve(byte protocol, ushort localPort);

    /// <summary>
    /// True when a real socket already holds this local address and port. Steering a connection to such an address
    /// would make its replies indistinguishable from that socket's own traffic.
    /// </summary>
    bool IsLocalPortInUse(byte protocol, IPAddress address, ushort port) => false;
}

public enum SteerVerdict { Pass, Rewritten }

/// <param name="Flows">Connections decided for this link since start (including those that stayed on their own link).</param>
/// <param name="BytesOut">Bytes of rewritten outbound packets; steered traffic only.</param>
/// <param name="BytesIn">Bytes of restored inbound packets; steered traffic only.</param>
public sealed record LinkSteerStats(string LinkId, long Flows, long BytesOut, long BytesIn, bool Quarantined);

/// <summary>
/// The packet-level half of system mode, without the driver: decides per connection which link to use and rewrites
/// the packets accordingly (source address out, destination address back in). The OS keeps believing the connection
/// uses its default address; only the wire sees the chosen link's address.
/// A connection is decided once, on its first packet, and never moves.
/// </summary>
public sealed class NatSteeringEngine
{
    private static readonly TimeSpan TcpIdle = TimeSpan.FromHours(2);
    private static readonly TimeSpan UdpIdle = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan EndedGrace = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan AnswerWait = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan Quarantine = TimeSpan.FromSeconds(60);
    private const int UnansweredLimit = 4;

    private sealed class Flow(FlowKey original, string linkId, IPAddress? steeredTo, long nowTicks)
    {
        public readonly FlowKey Original = original;
        public readonly string LinkId = linkId;
        public readonly IPAddress? SteeredTo = steeredTo;   // null when the flow stays on its own link
        public readonly long Created = nowTicks;
        public long LastSeen = nowTicks;
        public long EndedAt;                                // 0 = still open
        public volatile bool Answered;
    }

    private sealed class Counters
    {
        public long Flows, BytesOut, BytesIn;
        public long LastAnswered;
        public long QuarantinedUntil;
    }

    private readonly SteeringCoordinator _coordinator;
    private readonly IProcessResolver _resolver;
    private readonly Func<IReadOnlyList<LinkInfo>> _links;
    private readonly Func<IPAddress?> _defaultAddress;
    private readonly int _ownPid;
    private readonly Func<DateTimeOffset> _clock;

    private readonly ConcurrentDictionary<FlowKey, Flow> _outbound = new();
    private readonly ConcurrentDictionary<FlowKey, Flow> _inbound = new();
    private readonly ConcurrentDictionary<string, Counters> _counters = new();
    private readonly object _gate = new();

    public NatSteeringEngine(SteeringCoordinator coordinator, IProcessResolver resolver, Func<IReadOnlyList<LinkInfo>> links,
        Func<IPAddress?> defaultAddress, int ownPid, Func<DateTimeOffset> clock)
    {
        _coordinator = coordinator;
        _resolver = resolver;
        _links = links;
        _defaultAddress = defaultAddress;
        _ownPid = ownPid;
        _clock = clock;
    }

    public int ActiveFlows => _outbound.Count;

    public SteerVerdict ProcessOutbound(PacketView p)
    {
        if (!IsPublicUnicast(p.Destination)) return SteerVerdict.Pass;
        var native = _defaultAddress();
        if (native is null || !p.Source.Equals(native)) return SteerVerdict.Pass;

        var key = new FlowKey(p.Protocol, p.Source, p.SourcePort, p.Destination, p.DestinationPort);
        var now = _clock().UtcTicks;

        if (!_outbound.TryGetValue(key, out var flow))
        {
            // A TCP connection that is already running was not started by us; moving it would break it.
            if (p.Protocol == 6 && !p.IsTcpSyn) return SteerVerdict.Pass;
            flow = Decide(key, p, now);
            if (flow is null) return SteerVerdict.Pass;
        }

        Volatile.Write(ref flow.LastSeen, now);
        if (p.IsTcpEnd) Volatile.Write(ref flow.EndedAt, now);
        if (flow.SteeredTo is null) return SteerVerdict.Pass;

        p.SetSource(flow.SteeredTo);
        p.RecalculateChecksums();
        Interlocked.Add(ref Counter(flow.LinkId).BytesOut, p.Length);
        return SteerVerdict.Rewritten;
    }

    public SteerVerdict ProcessInbound(PacketView p)
    {
        var key = new FlowKey(p.Protocol, p.Destination, p.DestinationPort, p.Source, p.SourcePort);
        if (!_inbound.TryGetValue(key, out var flow)) return SteerVerdict.Pass;

        var now = _clock().UtcTicks;
        Volatile.Write(ref flow.LastSeen, now);
        if (p.IsTcpEnd) Volatile.Write(ref flow.EndedAt, now);
        if (!flow.Answered)
        {
            flow.Answered = true;
            Volatile.Write(ref Counter(flow.LinkId).LastAnswered, now);
        }

        p.SetDestination(flow.Original.LocalAddr);
        p.RecalculateChecksums();
        Interlocked.Add(ref Counter(flow.LinkId).BytesIn, p.Length);
        return SteerVerdict.Rewritten;
    }

    /// <summary>
    /// Takes a link out of rotation when connections steered to it are not being answered, so a link that cannot carry
    /// steered traffic (e.g. the phone drops spoofed-looking sources) never black-holes connections.
    /// </summary>
    public void CheckHealth()
    {
        var now = _clock().UtcTicks;
        var stuck = _outbound.Values
            .Where(f => f.SteeredTo is not null && !f.Answered && f.Original.Protocol == 6
                        && now - f.Created >= AnswerWait.Ticks)
            .GroupBy(f => f.LinkId);

        foreach (var group in stuck)
        {
            var counters = Counter(group.Key);
            var recentlyAnswered = now - Volatile.Read(ref counters.LastAnswered) < Quarantine.Ticks
                                   && Volatile.Read(ref counters.LastAnswered) != 0;
            if (recentlyAnswered || group.Count() < UnansweredLimit) continue;

            Volatile.Write(ref counters.QuarantinedUntil, now + Quarantine.Ticks);
            _coordinator.Exclude(group.Key, Quarantine);
            // Forget the stuck attempts so the operating system's SYN retransmissions are decided afresh.
            foreach (var flow in group) Forget(flow);
        }
    }

    /// <summary>Drops flows that are finished or idle for longer than their protocol allows; returns how many were removed.</summary>
    public int Cleanup()
    {
        _coordinator.Cleanup();
        var now = _clock().UtcTicks;
        var removed = 0;
        foreach (var flow in _outbound.Values)
        {
            var ended = Volatile.Read(ref flow.EndedAt);
            var idleLimit = (flow.Original.Protocol == 17 ? UdpIdle : TcpIdle).Ticks;
            var expired = now - Volatile.Read(ref flow.LastSeen) > idleLimit
                          || (ended != 0 && now - ended > EndedGrace.Ticks);
            if (!expired) continue;
            Forget(flow);
            removed++;
        }
        return removed;
    }

    public IReadOnlyList<LinkSteerStats> Stats()
    {
        var now = _clock().UtcTicks;
        return _links().Select(l =>
        {
            var c = Counter(l.Id);
            return new LinkSteerStats(l.Id, Volatile.Read(ref c.Flows), Volatile.Read(ref c.BytesOut),
                Volatile.Read(ref c.BytesIn), Volatile.Read(ref c.QuarantinedUntil) > now);
        }).ToList();
    }

    private Flow? Decide(FlowKey key, PacketView p, long now)
    {
        // Resolving the owner is slow, so do it outside the lock; only the decision itself must be serialised.
        var owner = _resolver.Resolve(p.Protocol, p.SourcePort);

        lock (_gate)
        {
            if (_outbound.TryGetValue(key, out var existing)) return existing;

            var links = _links();
            var native = links.FirstOrDefault(l => l.LocalAddress.Equals(key.LocalAddr));
            var own = owner?.Pid == _ownPid;
            var linkId = own ? null : _coordinator.PickLink(key, owner?.ExeName ?? "");
            var target = linkId is null ? null : links.FirstOrDefault(l => l.Id == linkId);

            if (target is not null && !own && !target.LocalAddress.Equals(key.LocalAddr)
                && _resolver.IsLocalPortInUse(key.Protocol, target.LocalAddress, key.LocalPort))
                target = null;   // port clash on the target link: keep this connection where it is

            if (own || target is null || target.LocalAddress.Equals(key.LocalAddr))
            {
                if (!own && linkId is null) return null;   // no measurements yet: leave routing to Windows, decide later
                var stay = new Flow(key, native?.Id ?? linkId ?? "", null, now);
                _outbound[key] = stay;
                if (!own) Interlocked.Increment(ref Counter(stay.LinkId).Flows);
                return stay;
            }

            var steered = new Flow(key, target.Id, target.LocalAddress, now);
            _outbound[key] = steered;
            _inbound[new FlowKey(key.Protocol, target.LocalAddress, key.LocalPort, key.RemoteAddr, key.RemotePort)] = steered;
            Interlocked.Increment(ref Counter(target.Id).Flows);
            return steered;
        }
    }

    private void Forget(Flow flow)
    {
        _outbound.TryRemove(flow.Original, out _);
        if (flow.SteeredTo is not null)
        {
            var k = flow.Original;
            _inbound.TryRemove(new FlowKey(k.Protocol, flow.SteeredTo, k.LocalPort, k.RemoteAddr, k.RemotePort), out _);
        }
    }

    private Counters Counter(string linkId) => _counters.GetOrAdd(linkId, _ => new Counters());

    /// <summary>Public unicast IPv4 only: LAN, loopback, link-local, CGNAT, multicast and broadcast are never steered.</summary>
    internal static bool IsPublicUnicast(IPAddress address)
    {
        Span<byte> b = stackalloc byte[4];
        if (!address.TryWriteBytes(b, out var n) || n != 4) return false;

        return b[0] switch
        {
            0 or 10 or 127 => false,
            >= 224 => false,
            100 => b[1] is < 64 or > 127,
            169 => b[1] != 254,
            172 => b[1] is < 16 or > 31,
            192 => b[1] != 168,
            _ => true,
        };
    }
}
