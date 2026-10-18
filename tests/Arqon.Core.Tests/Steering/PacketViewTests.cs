using System.Net;
using Arqon.Core.Steering;
using Arqon.Core.Tests.Support;

namespace Arqon.Core.Tests.Steering;

public sealed class PacketViewTests
{
    private static readonly IPAddress NewIp = IPAddress.Parse("10.99.0.7");

    [Fact]
    public void Parses_a_tcp_packet()
    {
        var raw = PacketBuilder.Build(PacketBuilder.Tcp, "192.168.1.5", 50000, "93.184.216.34", 443, PacketBuilder.FlagSyn);

        var p = PacketView.TryParse(raw, raw.Length);

        Assert.NotNull(p);
        Assert.Equal(6, p!.Protocol);
        Assert.Equal(IPAddress.Parse("192.168.1.5"), p.Source);
        Assert.Equal(IPAddress.Parse("93.184.216.34"), p.Destination);
        Assert.Equal(50000, p.SourcePort);
        Assert.Equal(443, p.DestinationPort);
        Assert.True(p.IsTcpSyn);
    }

    [Fact]
    public void A_syn_ack_is_not_a_connection_start()
    {
        var raw = PacketBuilder.Build(PacketBuilder.Tcp, "1.1.1.1", 443, "192.168.1.5", 50000,
            PacketBuilder.FlagSyn | PacketBuilder.FlagAck);

        Assert.False(PacketView.TryParse(raw, raw.Length)!.IsTcpSyn);
    }

    [Fact]
    public void Parses_a_udp_packet()
    {
        var raw = PacketBuilder.Build(PacketBuilder.Udp, "192.168.1.5", 40000, "8.8.8.8", 53);

        var p = PacketView.TryParse(raw, raw.Length);

        Assert.NotNull(p);
        Assert.Equal(17, p!.Protocol);
        Assert.Equal(53, p.DestinationPort);
        Assert.False(p.IsTcpSyn);
    }

    [Fact]
    public void Ignores_bytes_beyond_the_ip_total_length()
    {
        var raw = PacketBuilder.Build(PacketBuilder.Udp, "192.168.1.5", 40000, "8.8.8.8", 53);
        var padded = raw.Concat(new byte[10]).ToArray();

        Assert.Equal(raw.Length, PacketView.TryParse(padded, padded.Length)!.Length);
    }

    [Fact]
    public void Rejects_fragments_truncated_and_non_ipv4_data_without_throwing()
    {
        var fragment = PacketBuilder.Build(PacketBuilder.Udp, "192.168.1.5", 40000, "8.8.8.8", 53, fragment: true);
        Assert.Null(PacketView.TryParse(fragment, fragment.Length));

        var ok = PacketBuilder.Build(PacketBuilder.Tcp, "192.168.1.5", 50000, "1.2.3.4", 80);
        Assert.Null(PacketView.TryParse(ok, 10));          // shorter than an IP header
        Assert.Null(PacketView.TryParse(ok, 30));          // shorter than its own total length
        Assert.Null(PacketView.TryParse([], 0));

        var v6 = (byte[])ok.Clone();
        v6[0] = 0x60;
        Assert.Null(PacketView.TryParse(v6, v6.Length));

        var icmp = (byte[])ok.Clone();
        icmp[9] = 1;
        Assert.Null(PacketView.TryParse(icmp, icmp.Length));

        var badHeader = (byte[])ok.Clone();
        badHeader[0] = 0x4F;                                // header claims 60 bytes, packet has less transport left
        badHeader[2] = 0; badHeader[3] = 30;
        Assert.Null(PacketView.TryParse(badHeader, 30));
    }

    [Theory]
    [InlineData(PacketBuilder.Tcp)]
    [InlineData(PacketBuilder.Udp)]
    public void Rewriting_the_source_keeps_both_checksums_valid(byte protocol)
    {
        var raw = PacketBuilder.Build(protocol, "192.168.1.5", 50000, "93.184.216.34", 443);
        var p = PacketView.TryParse(raw, raw.Length)!;

        p.SetSource(NewIp);
        p.RecalculateChecksums();

        Assert.Equal("10.99.0.7", PacketBuilder.Source(raw));
        Assert.True(PacketBuilder.IpChecksumValid(raw));
        Assert.True(PacketBuilder.TransportChecksumValid(raw));
    }

    [Theory]
    [InlineData(PacketBuilder.Tcp)]
    [InlineData(PacketBuilder.Udp)]
    public void Rewriting_the_destination_keeps_both_checksums_valid(byte protocol)
    {
        var raw = PacketBuilder.Build(protocol, "93.184.216.34", 443, "10.99.0.7", 50000, payload: 13);
        var p = PacketView.TryParse(raw, raw.Length)!;

        p.SetDestination(IPAddress.Parse("192.168.1.5"));
        p.RecalculateChecksums();

        Assert.Equal("192.168.1.5", PacketBuilder.Destination(raw));
        Assert.True(PacketBuilder.IpChecksumValid(raw));
        Assert.True(PacketBuilder.TransportChecksumValid(raw));
    }

    [Fact]
    public void Recalculating_repairs_checksums_left_empty_by_offload()
    {
        var raw = PacketBuilder.Build(PacketBuilder.Tcp, "192.168.1.5", 50000, "93.184.216.34", 443);
        raw[10] = raw[11] = 0;   // IP checksum
        raw[36] = raw[37] = 0;   // TCP checksum
        var p = PacketView.TryParse(raw, raw.Length)!;

        p.RecalculateChecksums();

        Assert.True(PacketBuilder.IpChecksumValid(raw));
        Assert.True(PacketBuilder.TransportChecksumValid(raw));
    }
}
