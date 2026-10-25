using Arqon.Core.Relay;

namespace Arqon.Core.Tests.Relay;

public class ReorderBufferTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private static byte[] P(int n) => [(byte)n];
    private static int[] Values(IReadOnlyList<byte[]> pieces) => pieces.Select(p => (int)p[0]).ToArray();

    [Fact]
    public void Pieces_out_of_order_come_out_in_order()
    {
        var b = new ReorderBuffer(TimeSpan.FromMilliseconds(100));

        Assert.Empty(b.Push(1, P(1), T0));
        Assert.Empty(b.Push(2, P(2), T0));
        Assert.Equal([0, 1, 2], Values(b.Push(0, P(0), T0)));
        Assert.Equal(3ul, b.Next);
    }

    [Fact]
    public void A_duplicate_is_handed_on_once()
    {
        var b = new ReorderBuffer(TimeSpan.FromMilliseconds(100));

        Assert.Equal([0], Values(b.Push(0, P(0), T0)));
        Assert.Empty(b.Push(0, P(0), T0));
        Assert.Empty(b.Push(2, P(2), T0));
        Assert.Empty(b.Push(2, P(2), T0));
        Assert.Equal(2, b.Duplicates);
    }

    [Fact]
    public void A_lost_piece_is_skipped_after_the_wait()
    {
        var b = new ReorderBuffer(TimeSpan.FromMilliseconds(100));
        b.Push(0, P(0), T0);
        b.Push(2, P(2), T0);
        b.Push(3, P(3), T0);

        Assert.Empty(b.Flush(T0.AddMilliseconds(50)));
        Assert.Equal([2, 3], Values(b.Flush(T0.AddMilliseconds(150))));
        Assert.Equal(1, b.Skipped);
        Assert.Empty(b.Push(1, P(1), T0.AddMilliseconds(160)));   // too late: already skipped
    }

    [Fact]
    public void A_full_buffer_gives_up_on_the_oldest_gap()
    {
        var b = new ReorderBuffer(TimeSpan.FromHours(1), capacity: 3);

        b.Push(1, P(1), T0);
        b.Push(2, P(2), T0);
        b.Push(3, P(3), T0);
        var out_ = b.Push(4, P(4), T0);

        Assert.Equal([1, 2, 3, 4], Values(out_));
        Assert.Equal(1, b.Skipped);
    }
}
