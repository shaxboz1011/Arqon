using System.Buffers.Binary;
using System.Net;

namespace Arqon.Core.Steering;

/// <summary>
/// A mutable view over one IPv4 TCP or UDP packet. <see cref="TryParse"/> returns null for anything else
/// (IPv6, fragments, other protocols, truncated data) so callers can pass such packets through untouched.
/// </summary>
public sealed class PacketView
{
    private const int MinIpHeader = 20;
    private const int TcpFlagsOffset = 13;

    private readonly byte[] _buf;
    private readonly int _ihl;

    private PacketView(byte[] buffer, int length, int ipHeaderLength)
    {
        _buf = buffer;
        Length = length;
        _ihl = ipHeaderLength;
    }

    /// <summary>IP total length: the packet ends here even if the buffer is larger.</summary>
    public int Length { get; }

    public byte Protocol => _buf[9];
    public IPAddress Source => new(_buf.AsSpan(12, 4));
    public IPAddress Destination => new(_buf.AsSpan(16, 4));
    public ushort SourcePort => BinaryPrimitives.ReadUInt16BigEndian(_buf.AsSpan(_ihl));
    public ushort DestinationPort => BinaryPrimitives.ReadUInt16BigEndian(_buf.AsSpan(_ihl + 2));

    /// <summary>A TCP packet that opens a connection (SYN without ACK).</summary>
    public bool IsTcpSyn => Protocol == 6 && (_buf[_ihl + TcpFlagsOffset] & 0x12) == 0x02;

    /// <summary>A TCP packet that ends a connection (FIN or RST).</summary>
    public bool IsTcpEnd => Protocol == 6 && (_buf[_ihl + TcpFlagsOffset] & 0x05) != 0;

    public static PacketView? TryParse(byte[] buffer, int length)
    {
        if (length < MinIpHeader || length > buffer.Length) return null;
        if (buffer[0] >> 4 != 4) return null;

        var ihl = (buffer[0] & 0x0F) * 4;
        var total = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2));
        if (ihl < MinIpHeader || total > length || total < ihl) return null;

        // More-fragments flag or a non-zero offset: the transport header may be missing, so leave it alone.
        if ((BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(6)) & 0x3FFF) != 0) return null;

        var transport = total - ihl;
        switch (buffer[9])
        {
            case 6:
                if (transport < 20) return null;
                var dataOffset = (buffer[ihl + 12] >> 4) * 4;
                if (dataOffset < 20 || dataOffset > transport) return null;
                break;
            case 17:
                if (transport < 8) return null;
                break;
            default:
                return null;
        }

        return new PacketView(buffer, total, ihl);
    }

    public void SetSource(IPAddress address) => address.TryWriteBytes(_buf.AsSpan(12, 4), out _);

    public void SetDestination(IPAddress address) => address.TryWriteBytes(_buf.AsSpan(16, 4), out _);

    /// <summary>Recomputes the IP header and TCP/UDP checksums from scratch (captured packets often carry offloaded, empty ones).</summary>
    public void RecalculateChecksums()
    {
        _buf[10] = _buf[11] = 0;
        var ip = (ushort)~InternetChecksum.Fold(InternetChecksum.Sum(_buf.AsSpan(0, _ihl)));
        BinaryPrimitives.WriteUInt16BigEndian(_buf.AsSpan(10), ip);

        var at = _ihl + (Protocol == 6 ? 16 : 6);
        _buf[at] = _buf[at + 1] = 0;

        var segment = _buf.AsSpan(_ihl, Length - _ihl);
        var sum = InternetChecksum.Sum(_buf.AsSpan(12, 8)) + Protocol + (uint)segment.Length + InternetChecksum.Sum(segment);
        var checksum = (ushort)~InternetChecksum.Fold(sum);
        if (checksum == 0 && Protocol == 17) checksum = 0xFFFF; // zero means "no checksum" for UDP
        BinaryPrimitives.WriteUInt16BigEndian(_buf.AsSpan(at), checksum);
    }
}

internal static class InternetChecksum
{
    /// <summary>Sum of 16-bit big-endian words; an odd trailing byte counts as the high half of a word.</summary>
    public static uint Sum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        for (var i = 0; i + 1 < data.Length; i += 2) sum += (uint)(data[i] << 8 | data[i + 1]);
        if (data.Length % 2 == 1) sum += (uint)(data[^1] << 8);
        return sum;
    }

    public static uint Fold(uint sum)
    {
        while (sum >> 16 != 0) sum = (sum & 0xFFFF) + (sum >> 16);
        return sum;
    }
}
