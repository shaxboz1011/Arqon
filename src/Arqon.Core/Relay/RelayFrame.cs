using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Arqon.Core.Relay;

public enum RelayFrameType : byte
{
    /// <summary>A piece of the tunnelled stream, sent over one link.</summary>
    Data = 1,
    /// <summary>The same piece sent over several links (game packets): the first copy to arrive wins.</summary>
    Duplicate = 2,
    /// <summary>The highest sequence number received in order, so the sender can forget older pieces.</summary>
    Ack = 3,
    /// <summary>Keeps a link's NAT mapping open and measures its round trip.</summary>
    KeepAlive = 4,
}

/// <summary>One frame of the relay protocol (see docs/relay.md).</summary>
public sealed record RelayFrame(RelayFrameType Type, uint Session, ulong Sequence, byte LinkIndex, ReadOnlyMemory<byte> Payload);

/// <summary>
/// Turns frames into bytes and back. Layout (big-endian): version (1), type (1), link (1), reserved (1), session (4),
/// sequence (8), payload length (2), payload, then the first 16 bytes of HMAC-SHA256 over everything before it.
/// A frame with a wrong tag, version or length is rejected: a stranger who guesses the relay's port cannot inject data.
/// </summary>
public sealed class RelayCodec
{
    public const byte Version = 1;
    public const int HeaderSize = 18;
    public const int TagSize = 16;
    public const int MaxPayload = 1400;   // fits one UDP datagram on a 1500-byte path with room for IP, UDP and a tunnel

    private readonly byte[] _key;

    public RelayCodec(byte[] key)
    {
        if (key.Length < 32) throw new ArgumentException("The key needs at least 32 bytes", nameof(key));
        _key = key.ToArray();
    }

    public byte[] Encode(RelayFrame frame)
    {
        if (frame.Payload.Length > MaxPayload) throw new ArgumentException("The payload is too big for one frame", nameof(frame));
        var bytes = new byte[HeaderSize + frame.Payload.Length + TagSize];
        var span = bytes.AsSpan();
        span[0] = Version;
        span[1] = (byte)frame.Type;
        span[2] = frame.LinkIndex;
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], frame.Session);
        BinaryPrimitives.WriteUInt64BigEndian(span[8..], frame.Sequence);
        BinaryPrimitives.WriteUInt16BigEndian(span[16..], (ushort)frame.Payload.Length);
        frame.Payload.Span.CopyTo(span[HeaderSize..]);
        var signed = HeaderSize + frame.Payload.Length;
        HMACSHA256.HashData(_key, span[..signed])[..TagSize].CopyTo(span[signed..]);
        return bytes;
    }

    public bool TryDecode(ReadOnlySpan<byte> bytes, out RelayFrame? frame)
    {
        frame = null;
        if (bytes.Length < HeaderSize + TagSize || bytes[0] != Version) return false;
        var length = BinaryPrimitives.ReadUInt16BigEndian(bytes[16..]);
        if (length > MaxPayload || bytes.Length != HeaderSize + length + TagSize) return false;

        var signed = HeaderSize + length;
        Span<byte> expected = stackalloc byte[32];
        HMACSHA256.HashData(_key, bytes[..signed], expected);
        if (!CryptographicOperations.FixedTimeEquals(expected[..TagSize], bytes.Slice(signed, TagSize))) return false;

        var type = (RelayFrameType)bytes[1];
        if (!Enum.IsDefined(type)) return false;
        frame = new RelayFrame(type, BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]), BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]),
            bytes[2], bytes.Slice(HeaderSize, length).ToArray());
        return true;
    }
}
