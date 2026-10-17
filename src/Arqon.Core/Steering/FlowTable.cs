using System.Collections.Concurrent;
using System.Net;

namespace Arqon.Core.Steering;

/// <summary>One network connection as the packet layer sees it. Protocol is the IP protocol number (6 = TCP, 17 = UDP).</summary>
public readonly record struct FlowKey(byte Protocol, IPAddress LocalAddr, ushort LocalPort, IPAddress RemoteAddr, ushort RemotePort);

/// <summary>Remembers which link each flow was given, so a connection never changes link mid-way (that would break it).</summary>
public sealed class FlowTable(Func<DateTimeOffset> clock)
{
    private static readonly TimeSpan TcpIdle = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan UdpIdle = TimeSpan.FromSeconds(30);

    private sealed class Entry(string linkId, DateTimeOffset seen)
    {
        public readonly string LinkId = linkId;
        public DateTimeOffset LastSeen = seen;
    }

    private readonly ConcurrentDictionary<FlowKey, Entry> _flows = new();

    public int Count => _flows.Count;

    public bool TryGet(FlowKey key, out string linkId)
    {
        if (_flows.TryGetValue(key, out var e))
        {
            e.LastSeen = clock();
            linkId = e.LinkId;
            return true;
        }
        linkId = "";
        return false;
    }

    public void Set(FlowKey key, string linkId) => _flows[key] = new Entry(linkId, clock());

    /// <summary>Drops flows idle for longer than their protocol allows; returns how many were removed.</summary>
    public int RemoveExpired()
    {
        var now = clock();
        var removed = 0;
        foreach (var (key, entry) in _flows)
        {
            var limit = key.Protocol == 17 ? UdpIdle : TcpIdle;
            if (now - entry.LastSeen > limit && _flows.TryRemove(key, out _)) removed++;
        }
        return removed;
    }
}
