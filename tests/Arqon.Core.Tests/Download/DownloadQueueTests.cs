using Arqon.Core.Download;

namespace Arqon.Core.Tests.Download;

public class DownloadQueueTests
{
    [Fact]
    public void Downloads_start_at_once_while_there_is_room_and_wait_in_order_after_that()
    {
        var q = new DownloadQueue(2);

        Assert.Equal(["a"], q.Add("a"));
        Assert.Equal(["b"], q.Add("b"));
        Assert.Empty(q.Add("c"));
        Assert.Empty(q.Add("d"));

        Assert.Equal(["a", "b"], q.Running.Order());
        Assert.Equal(["c", "d"], q.Waiting);
    }

    [Fact]
    public void A_finished_download_makes_room_for_the_next_one_in_line()
    {
        var q = new DownloadQueue(1);
        q.Add("a"); q.Add("b"); q.Add("c");

        Assert.Equal(["b"], q.Finished("a"));
        Assert.Equal(["c"], q.Finished("b"));
        Assert.Empty(q.Finished("c"));
        Assert.Empty(q.Running);
        Assert.Empty(q.Waiting);
    }

    [Fact]
    public void Removing_a_waiting_download_just_drops_it_from_the_line()
    {
        var q = new DownloadQueue(1);
        q.Add("a"); q.Add("b"); q.Add("c");

        Assert.Empty(q.Remove("b"));

        Assert.Equal(["c"], q.Waiting);
        Assert.Equal(["a"], q.Running);
    }

    [Fact]
    public void Removing_a_running_download_frees_its_place()
    {
        var q = new DownloadQueue(1);
        q.Add("a"); q.Add("b");

        Assert.Equal(["b"], q.Remove("a"));
    }

    [Fact]
    public void Raising_the_limit_starts_waiting_downloads_at_once()
    {
        var q = new DownloadQueue(1);
        q.Add("a"); q.Add("b"); q.Add("c"); q.Add("d");

        Assert.Equal(["b", "c"], q.SetLimit(3));
        Assert.Equal(["d"], q.Waiting);
    }

    [Fact]
    public void Lowering_the_limit_never_stops_a_running_download_but_nothing_new_starts_until_there_is_room()
    {
        var q = new DownloadQueue(3);
        q.Add("a"); q.Add("b"); q.Add("c"); q.Add("d");

        Assert.Empty(q.SetLimit(1));
        Assert.Equal(3, q.Running.Count);

        Assert.Empty(q.Finished("a"));   // 2 still running, limit 1
        Assert.Empty(q.Finished("b"));   // 1 still running, limit 1
        Assert.Equal(["d"], q.Finished("c"));
    }

    [Fact]
    public void Start_now_lets_a_waiting_download_jump_the_line_even_when_the_limit_is_reached()
    {
        var q = new DownloadQueue(1);
        q.Add("a"); q.Add("b"); q.Add("c");

        Assert.Equal(["c"], q.StartNow("c"));

        Assert.Equal(["a", "c"], q.Running.Order());
        Assert.Equal(["b"], q.Waiting);
        Assert.Empty(q.Finished("a"));   // still over the limit: b keeps waiting
        Assert.Equal(["b"], q.Finished("c"));
    }

    [Fact]
    public void Start_now_does_nothing_for_a_download_that_is_not_waiting()
    {
        var q = new DownloadQueue(1);
        q.Add("a");

        Assert.Empty(q.StartNow("a"));
        Assert.Empty(q.StartNow("nope"));
    }

    [Fact]
    public void The_same_download_is_never_queued_twice()
    {
        var q = new DownloadQueue(1);
        q.Add("a"); q.Add("b");

        Assert.Empty(q.Add("a"));
        Assert.Empty(q.Add("b"));

        Assert.Equal(["a"], q.Running);
        Assert.Equal(["b"], q.Waiting);
    }

    [Fact]
    public void Unknown_ids_are_harmless()
    {
        var q = new DownloadQueue(2);

        Assert.Empty(q.Finished("ghost"));
        Assert.Empty(q.Remove("ghost"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void The_limit_is_at_least_one(int asked)
    {
        var q = new DownloadQueue(asked);

        Assert.Equal(1, q.Limit);
        Assert.Equal(1, q.SetLimit(asked).Count + q.Limit);   // nothing waiting: no starts, limit still 1
    }

    [Fact]
    public void Position_counts_from_one_for_waiting_downloads_and_is_zero_otherwise()
    {
        var q = new DownloadQueue(1);
        q.Add("a"); q.Add("b"); q.Add("c");

        Assert.Equal(0, q.PositionOf("a"));
        Assert.Equal(1, q.PositionOf("b"));
        Assert.Equal(2, q.PositionOf("c"));
        Assert.Equal(0, q.PositionOf("nope"));
    }

    [Fact]
    public void Ids_are_compared_case_insensitively_like_windows_paths()
    {
        var q = new DownloadQueue(1);
        q.Add(@"C:\Dl\File.zip");

        Assert.Empty(q.Add(@"c:\dl\file.ZIP"));
        Assert.True(q.IsRunning(@"C:\DL\FILE.ZIP"));
    }
}
