using Arqon.Core.Relay;

namespace Arqon.Core.Tests.Relay;

public class RelayCodecTests
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private readonly RelayCodec _codec = new(Key);

    [Fact]
    public void A_frame_survives_the_trip()
    {
        var bytes = _codec.Encode(new RelayFrame(RelayFrameType.Duplicate, 0xA1B2C3D4, 1234567890123, 2, new byte[] { 1, 2, 3 }));

        Assert.True(_codec.TryDecode(bytes, out var frame));
        Assert.Equal(RelayFrameType.Duplicate, frame!.Type);
        Assert.Equal(0xA1B2C3D4u, frame.Session);
        Assert.Equal(1234567890123ul, frame.Sequence);
        Assert.Equal(2, frame.LinkIndex);
        Assert.Equal(new byte[] { 1, 2, 3 }, frame.Payload.ToArray());
        Assert.Equal(RelayCodec.HeaderSize + 3 + RelayCodec.TagSize, bytes.Length);
    }

    [Fact]
    public void A_changed_byte_or_another_key_is_rejected()
    {
        var bytes = _codec.Encode(new RelayFrame(RelayFrameType.Data, 1, 1, 0, new byte[] { 9, 9 }));
        var tampered = bytes.ToArray();
        tampered[RelayCodec.HeaderSize] ^= 1;

        Assert.False(_codec.TryDecode(tampered, out _));
        Assert.False(new RelayCodec(new byte[32]).TryDecode(bytes, out _));
    }

    [Fact]
    public void Short_long_or_unknown_frames_are_rejected()
    {
        var bytes = _codec.Encode(new RelayFrame(RelayFrameType.Ack, 1, 5, 0, ReadOnlyMemory<byte>.Empty));

        Assert.False(_codec.TryDecode(bytes.AsSpan(0, bytes.Length - 1), out _));
        Assert.False(_codec.TryDecode([.. bytes, 0], out _));
        var wrongVersion = bytes.ToArray();
        wrongVersion[0] = 9;
        Assert.False(_codec.TryDecode(wrongVersion, out _));
    }

    [Fact]
    public void Too_big_a_payload_or_too_short_a_key_is_refused()
    {
        Assert.Throws<ArgumentException>(() => _codec.Encode(new RelayFrame(RelayFrameType.Data, 1, 1, 0, new byte[RelayCodec.MaxPayload + 1])));
        Assert.Throws<ArgumentException>(() => new RelayCodec(new byte[16]));
    }
}
