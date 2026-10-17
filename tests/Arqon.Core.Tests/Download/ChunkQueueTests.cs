using Arqon.Core.Download;

namespace Arqon.Core.Tests.Download;

public class ChunkQueueTests
{
    [Fact]
    public void Split_covers_length_with_a_short_last_chunk()
    {
        var ranges = ChunkPlan.Split(10, 4);
        Assert.Equal([new ByteRange(0, 3), new ByteRange(4, 7), new ByteRange(8, 9)], ranges);
    }

    [Fact]
    public void Split_of_empty_file_is_empty()
    {
        Assert.Empty(ChunkPlan.Split(0, 4));
    }

    [Fact]
    public void Split_has_no_gaps_or_overlaps_for_random_lengths()
    {
        var rng = new Random(42);
        for (var i = 0; i < 200; i++)
        {
            var length = rng.NextInt64(1, 100_000);
            var chunk = rng.NextInt64(1, 5_000);
            var ranges = ChunkPlan.Split(length, chunk);

            Assert.Equal(0, ranges[0].Start);
            Assert.Equal(length - 1, ranges[^1].End);
            for (var j = 1; j < ranges.Count; j++)
                Assert.Equal(ranges[j - 1].End + 1, ranges[j].Start);
            Assert.Equal(length, ranges.Sum(r => r.Length));
        }
    }

    [Fact]
    public void Returned_range_is_taken_next()
    {
        var q = new ChunkQueue(ChunkPlan.Split(12, 4));
        Assert.True(q.TryTake(out var first));
        Assert.True(q.TryTake(out _));
        q.Return(first);

        Assert.True(q.TryTake(out var again));
        Assert.Equal(first, again);
    }

    [Fact]
    public async Task Concurrent_takers_get_every_chunk_exactly_once()
    {
        var q = new ChunkQueue(ChunkPlan.Split(100_000, 10));
        var taken = new System.Collections.Concurrent.ConcurrentBag<ByteRange>();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            while (q.TryTake(out var r)) taken.Add(r);
        })));

        Assert.Equal(10_000, taken.Count);
        Assert.Equal(10_000, taken.Distinct().Count());
        Assert.Equal(0, q.Remaining);
    }
}
