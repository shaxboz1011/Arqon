using System.Net;
using Arqon.Core.Links;
using Arqon.Core.Monitoring;
using Arqon.Core.Policy;
using Arqon.Core.Settings;
using Arqon.Core.Steering;
using Arqon.Core.Tests.Support;

namespace Arqon.Core.Tests.Steering;

public class NatSteeringEngineTests
{
    private const string WifiIp = "192.168.1.5", PhoneIp = "192.168.42.129", Server = "93.184.216.34";
    private const int OwnPid = 4242;

    private sealed class NoSteerer : ISteerer
    {
        public bool IsAvailable => true;
        public void Start() { }
        public void Stop() { }
        public event Action<string>? Faulted { add { } remove { } }
    }

    private sealed class FakeResolver : IProcessResolver
    {
        public readonly Dictionary<ushort, ProcessInfo> Ports = new();
        public ProcessInfo? Resolve(byte protocol, ushort localPort) => Ports.GetValueOrDefault(localPort);
    }

    private sealed class Rig
    {
        public DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        public readonly FakeResolver Resolver = new();
        public readonly SteeringCoordinator Coordinator;
        public readonly NatSteeringEngine Engine;
        public IPAddress? Default = IPAddress.Parse(WifiIp);

        public Rig()
        {
            LinkInfo[] links =
            [
                new("wifi", "Wi-Fi", "", IPAddress.Parse(WifiIp), IPAddress.Parse("192.168.1.1"), LinkKind.Wifi, 0),
                new("phone", "Phone", "", IPAddress.Parse(PhoneIp), IPAddress.Parse("192.168.42.1"), LinkKind.Mobile, 0),
            ];
            LinkSample[] samples =
            [
                new("wifi", DateTimeOffset.UnixEpoch, 20, 1, 0, 1_000_000, 0),
                new("phone", DateTimeOffset.UnixEpoch, 30, 1, 0, 1_000_000, 0),
            ];
            var settings = ArqonSettings.Default() with
            {
                Rules =
                [
                    new AppRule("chrome.exe", AppKind.Normal, LinkChoice.Only, "phone", null),
                    new AppRule("edge.exe", AppKind.Normal, LinkChoice.Only, "wifi", null),
                ],
            };
            Coordinator = new SteeringCoordinator(new NoSteerer(), new FlowTable(() => Now), new RuleEngine(settings),
                () => samples, () => Now);
            Engine = new NatSteeringEngine(Coordinator, Resolver, () => links, () => Default, OwnPid, () => Now);

            Resolver.Ports[50000] = new ProcessInfo(100, "chrome.exe");
            Resolver.Ports[50001] = new ProcessInfo(101, "edge.exe");
            Resolver.Ports[50002] = new ProcessInfo(OwnPid, "arqon.exe");
        }

        public (byte[] Raw, PacketView View) Packet(byte proto, ushort srcPort, string dst = Server, ushort dstPort = 443,
            byte flags = PacketBuilder.FlagSyn, string src = WifiIp)
        {
            var raw = PacketBuilder.Build(proto, src, srcPort, dst, dstPort, flags);
            return (raw, PacketView.TryParse(raw, raw.Length)!);
        }

        public (byte[] Raw, PacketView View) Reply(byte proto, ushort localPort, string localIp = PhoneIp, string from = Server,
            ushort fromPort = 443, byte flags = PacketBuilder.FlagSyn | PacketBuilder.FlagAck)
        {
            var raw = PacketBuilder.Build(proto, from, fromPort, localIp, localPort, flags);
            return (raw, PacketView.TryParse(raw, raw.Length)!);
        }
    }

    [Fact]
    public void A_connection_ruled_to_the_phone_leaves_with_the_phone_address()
    {
        var rig = new Rig();
        var (raw, view) = rig.Packet(PacketBuilder.Tcp, 50000);

        Assert.Equal(SteerVerdict.Rewritten, rig.Engine.ProcessOutbound(view));

        Assert.Equal(PhoneIp, PacketBuilder.Source(raw));
        Assert.True(PacketBuilder.IpChecksumValid(raw));
        Assert.True(PacketBuilder.TransportChecksumValid(raw));
    }

    [Fact]
    public void Later_packets_of_the_same_connection_follow_the_first()
    {
        var rig = new Rig();
        rig.Engine.ProcessOutbound(rig.Packet(PacketBuilder.Tcp, 50000).View);

        var (raw, view) = rig.Packet(PacketBuilder.Tcp, 50000, flags: PacketBuilder.FlagAck);

        Assert.Equal(SteerVerdict.Rewritten, rig.Engine.ProcessOutbound(view));
        Assert.Equal(PhoneIp, PacketBuilder.Source(raw));
    }

    [Fact]
    public void A_connection_that_stays_on_its_own_link_is_left_byte_for_byte_alone()
    {
        var rig = new Rig();
        var (raw, view) = rig.Packet(PacketBuilder.Tcp, 50001);
        var before = (byte[])raw.Clone();

        Assert.Equal(SteerVerdict.Pass, rig.Engine.ProcessOutbound(view));
        Assert.Equal(before, raw);
    }

    [Fact]
    public void Replies_to_a_steered_connection_are_sent_back_to_the_original_address()
    {
        var rig = new Rig();
        rig.Engine.ProcessOutbound(rig.Packet(PacketBuilder.Tcp, 50000).View);
        var (raw, view) = rig.Reply(PacketBuilder.Tcp, 50000);

        Assert.Equal(SteerVerdict.Rewritten, rig.Engine.ProcessInbound(view));

        Assert.Equal(WifiIp, PacketBuilder.Destination(raw));
        Assert.True(PacketBuilder.IpChecksumValid(raw));
        Assert.True(PacketBuilder.TransportChecksumValid(raw));
    }

    [Fact]
    public void Inbound_traffic_that_was_never_steered_passes_untouched()
    {
        var rig = new Rig();
        var (raw, view) = rig.Reply(PacketBuilder.Tcp, 50000);
        var before = (byte[])raw.Clone();

        Assert.Equal(SteerVerdict.Pass, rig.Engine.ProcessInbound(view));
        Assert.Equal(before, raw);
    }

    [Fact]
    public void A_reply_from_a_different_server_is_not_mistaken_for_a_steered_flow()
    {
        var rig = new Rig();
        rig.Engine.ProcessOutbound(rig.Packet(PacketBuilder.Tcp, 50000).View);
        var (_, view) = rig.Reply(PacketBuilder.Tcp, 50000, from: "8.8.8.8");

        Assert.Equal(SteerVerdict.Pass, rig.Engine.ProcessInbound(view));
    }

    [Fact]
    public void A_tcp_connection_that_started_before_steering_is_never_moved()
    {
        var rig = new Rig();
        var (raw, view) = rig.Packet(PacketBuilder.Tcp, 50000, flags: PacketBuilder.FlagAck);
        var before = (byte[])raw.Clone();

        Assert.Equal(SteerVerdict.Pass, rig.Engine.ProcessOutbound(view));
        Assert.Equal(before, raw);
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("10.0.0.8")]
    [InlineData("172.20.1.1")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.3.4")]
    [InlineData("100.64.0.9")]
    [InlineData("224.0.0.251")]
    [InlineData("255.255.255.255")]
    public void Local_and_non_public_destinations_are_never_steered(string destination)
    {
        var rig = new Rig();
        var (raw, view) = rig.Packet(PacketBuilder.Tcp, 50000, dst: destination);
        var before = (byte[])raw.Clone();

        Assert.Equal(SteerVerdict.Pass, rig.Engine.ProcessOutbound(view));
        Assert.Equal(before, raw);
    }

    [Fact]
    public void Arqons_own_traffic_is_never_steered()
    {
        var rig = new Rig();
        var (raw, view) = rig.Packet(PacketBuilder.Tcp, 50002);
        var before = (byte[])raw.Clone();

        Assert.Equal(SteerVerdict.Pass, rig.Engine.ProcessOutbound(view));
        Assert.Equal(before, raw);
    }

    [Fact]
    public void Traffic_not_sent_from_the_default_address_is_not_touched()
    {
        var rig = new Rig();
        var (raw, view) = rig.Packet(PacketBuilder.Tcp, 50000, src: PhoneIp);
        var before = (byte[])raw.Clone();

        Assert.Equal(SteerVerdict.Pass, rig.Engine.ProcessOutbound(view));
        Assert.Equal(before, raw);
    }

    [Fact]
    public void Nothing_is_steered_while_the_default_address_is_unknown()
    {
        var rig = new Rig { Default = null };
        var (_, view) = rig.Packet(PacketBuilder.Tcp, 50000);

        Assert.Equal(SteerVerdict.Pass, rig.Engine.ProcessOutbound(view));
    }

    [Fact]
    public void A_flow_from_an_unknown_process_is_still_balanced()
    {
        var rig = new Rig();
        var links = Enumerable.Range(0, 40)
            .Select(i => rig.Packet(PacketBuilder.Tcp, (ushort)(51000 + i)))
            .Select(p => rig.Engine.ProcessOutbound(p.View) == SteerVerdict.Rewritten ? "phone" : "wifi")
            .ToHashSet();

        Assert.Equal(2, links.Count);
    }

    [Fact]
    public void The_first_udp_packet_decides_the_flow()
    {
        var rig = new Rig();
        var (raw, view) = rig.Packet(PacketBuilder.Udp, 50000, dstPort: 443);

        Assert.Equal(SteerVerdict.Rewritten, rig.Engine.ProcessOutbound(view));
        Assert.Equal(PhoneIp, PacketBuilder.Source(raw));
        Assert.True(PacketBuilder.TransportChecksumValid(raw));

        var (reply, replyView) = rig.Reply(PacketBuilder.Udp, 50000);
        Assert.Equal(SteerVerdict.Rewritten, rig.Engine.ProcessInbound(replyView));
        Assert.Equal(WifiIp, PacketBuilder.Destination(reply));
    }

    [Fact]
    public void Idle_flows_are_forgotten_so_the_table_cannot_grow_forever()
    {
        var rig = new Rig();
        rig.Engine.ProcessOutbound(rig.Packet(PacketBuilder.Udp, 50000).View);
        rig.Now += TimeSpan.FromMinutes(5);

        Assert.Equal(1, rig.Engine.Cleanup());

        var (raw, view) = rig.Reply(PacketBuilder.Udp, 50000);
        var before = (byte[])raw.Clone();
        Assert.Equal(SteerVerdict.Pass, rig.Engine.ProcessInbound(view));
        Assert.Equal(before, raw);
    }

    [Fact]
    public void A_long_idle_tcp_connection_is_kept_because_dropping_it_would_break_it()
    {
        var rig = new Rig();
        rig.Engine.ProcessOutbound(rig.Packet(PacketBuilder.Tcp, 50000).View);
        rig.Now += TimeSpan.FromMinutes(30);

        Assert.Equal(0, rig.Engine.Cleanup());
    }

    [Fact]
    public void A_finished_tcp_connection_is_forgotten_after_a_grace_period()
    {
        var rig = new Rig();
        rig.Engine.ProcessOutbound(rig.Packet(PacketBuilder.Tcp, 50000).View);
        rig.Engine.ProcessOutbound(rig.Packet(PacketBuilder.Tcp, 50000, flags: PacketBuilder.FlagFin | PacketBuilder.FlagAck).View);
        rig.Now += TimeSpan.FromMinutes(2);

        Assert.Equal(1, rig.Engine.Cleanup());
    }

    [Fact]
    public void A_link_that_never_answers_is_taken_out_of_rotation()
    {
        var rig = new Rig();
        for (ushort i = 0; i < 5; i++)
        {
            rig.Resolver.Ports[(ushort)(52000 + i)] = new ProcessInfo(100, "chrome.exe");
            rig.Engine.ProcessOutbound(rig.Packet(PacketBuilder.Tcp, (ushort)(52000 + i)).View);
        }

        rig.Now += TimeSpan.FromSeconds(10);
        rig.Engine.CheckHealth();

        // chrome is ruled "Only phone", but a dead phone must fall back to the working link
        var (raw, view) = rig.Packet(PacketBuilder.Tcp, 50000);
        Assert.Equal(SteerVerdict.Pass, rig.Engine.ProcessOutbound(view));
        Assert.Equal(WifiIp, PacketBuilder.Source(raw));
        Assert.True(rig.Engine.Stats().Single(s => s.LinkId == "phone").Quarantined);
    }

    [Fact]
    public void A_stuck_connection_is_decided_again_when_its_link_is_taken_out()
    {
        var rig = new Rig();
        for (ushort i = 0; i < 5; i++)
        {
            rig.Resolver.Ports[(ushort)(52000 + i)] = new ProcessInfo(100, "chrome.exe");
            rig.Engine.ProcessOutbound(rig.Packet(PacketBuilder.Tcp, (ushort)(52000 + i)).View);
        }
        rig.Now += TimeSpan.FromSeconds(10);
        rig.Engine.CheckHealth();

        // the operating system retransmits the SYN of the first connection
        var (raw, view) = rig.Packet(PacketBuilder.Tcp, 52000);
        Assert.Equal(SteerVerdict.Pass, rig.Engine.ProcessOutbound(view));
        Assert.Equal(WifiIp, PacketBuilder.Source(raw));
    }

    [Fact]
    public void A_link_that_answers_stays_in_rotation()
    {
        var rig = new Rig();
        for (ushort i = 0; i < 5; i++)
        {
            rig.Resolver.Ports[(ushort)(52000 + i)] = new ProcessInfo(100, "chrome.exe");
            rig.Engine.ProcessOutbound(rig.Packet(PacketBuilder.Tcp, (ushort)(52000 + i)).View);
        }
        rig.Engine.ProcessInbound(rig.Reply(PacketBuilder.Tcp, 52002).View);

        rig.Now += TimeSpan.FromSeconds(10);
        rig.Engine.CheckHealth();

        Assert.False(rig.Engine.Stats().Single(s => s.LinkId == "phone").Quarantined);
    }

    [Fact]
    public void A_few_unanswered_connections_are_not_enough_to_blame_a_link()
    {
        var rig = new Rig();
        rig.Engine.ProcessOutbound(rig.Packet(PacketBuilder.Tcp, 50000).View);
        rig.Now += TimeSpan.FromSeconds(10);

        rig.Engine.CheckHealth();

        Assert.False(rig.Engine.Stats().Single(s => s.LinkId == "phone").Quarantined);
    }

    [Fact]
    public void Statistics_count_flows_and_bytes_per_link()
    {
        var rig = new Rig();
        var (_, out1) = rig.Packet(PacketBuilder.Tcp, 50000);
        rig.Engine.ProcessOutbound(out1);
        var (_, in1) = rig.Reply(PacketBuilder.Tcp, 50000);
        rig.Engine.ProcessInbound(in1);

        var phone = rig.Engine.Stats().Single(s => s.LinkId == "phone");

        Assert.Equal(1, phone.Flows);
        Assert.Equal(out1.Length, phone.BytesOut);
        Assert.Equal(in1.Length, phone.BytesIn);
    }
}
