using System.Buffers.Binary;
using System.Net;

namespace Arqon.Core.Tests.Support;

/// <summary>Builds well-formed IPv4 TCP/UDP packets for tests, and verifies checksums independently of the production code.</summary>
public static class PacketBuilder
{
    public const byte Tcp = 6, Udp = 17;
    public const byte FlagFin = 0x01, FlagSyn = 0x02, FlagRst = 0x04, FlagAck = 0x10;

    public static byte[] Build(byte protocol, string src, ushort srcPort, string dst, ushort dstPort,
        byte tcpFlags = FlagAck, int payload = 12, bool fragment = false)
    {
        var transport = protocol == Tcp ? 20 : 8;
        var total = 20 + transport + payload;
        var p = new byte[total];
        p[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(2), (ushort)total);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(4), 0x1234);
        if (fragment) BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(6), 0x2000); // more-fragments
        p[8] = 64;
        p[9] = protocol;
        IPAddress.Parse(src).TryWriteBytes(p.AsSpan(12), out _);
        IPAddress.Parse(dst).TryWriteBytes(p.AsSpan(16), out _);

        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(20), srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(22), dstPort);
        if (protocol == Tcp)
        {
            p[32] = 0x50; // data offset 5 words
            p[33] = tcpFlags;
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(24), (ushort)(8 + payload));
        }
        for (var i = 0; i < payload; i++) p[20 + transport + i] = (byte)(i * 7 + 3);

        Seal(p);
        return p;
    }

    /// <summary>Writes correct IP and transport checksums using a deliberately simple, separate implementation.</summary>
    public static void Seal(byte[] p)
    {
        p[10] = p[11] = 0;
        var ip = Fold(Sum(p.AsSpan(0, 20)));
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(10), (ushort)~ip);

        var at = p[9] == Tcp ? 36 : 26;
        p[at] = p[at + 1] = 0;
        var c = (ushort)~TransportSum(p);
        if (c == 0 && p[9] == Udp) c = 0xFFFF;
        BinaryPrimitives.WriteUInt16BigEndian(p.AsSpan(at), c);
    }

    public static bool IpChecksumValid(byte[] p) => Fold(Sum(p.AsSpan(0, 20))) == 0xFFFF;

    public static bool TransportChecksumValid(byte[] p) => TransportSum(p) == 0xFFFF;

    public static string Source(byte[] p) => new IPAddress(p.AsSpan(12, 4)).ToString();
    public static string Destination(byte[] p) => new IPAddress(p.AsSpan(16, 4)).ToString();

    private static uint TransportSum(byte[] p)
    {
        var len = p.Length - 20;
        uint sum = Sum(p.AsSpan(12, 8)) + p[9] + (uint)len;
        sum += Sum(p.AsSpan(20, len));
        return Fold(sum);
    }

    private static uint Sum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        for (var i = 0; i + 1 < data.Length; i += 2) sum += (uint)(data[i] << 8 | data[i + 1]);
        if (data.Length % 2 == 1) sum += (uint)(data[^1] << 8);
        return sum;
    }

    private static uint Fold(uint sum)
    {
        while (sum >> 16 != 0) sum = (sum & 0xFFFF) + (sum >> 16);
        return sum;
    }
}
