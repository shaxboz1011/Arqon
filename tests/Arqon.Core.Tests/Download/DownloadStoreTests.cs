using Arqon.Core.Download;

namespace Arqon.Core.Tests.Download;

public sealed class DownloadStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arqon-store-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void Missing_file_gives_empty_list()
    {
        Assert.Empty(new DownloadStore(Path.Combine(_dir, "d.json")).Load());
    }

    [Fact]
    public void Records_round_trip()
    {
        var store = new DownloadStore(Path.Combine(_dir, "nested", "d.json"));
        var records = new[]
        {
            new DownloadRecord("https://example.com/a.iso", @"C:\Downloads\a.iso", "a.iso", false),
            new DownloadRecord("https://example.com/b.zip", @"C:\Downloads\b.zip", "b.zip", true),
        };

        store.Save(records);

        Assert.Equal(records, store.Load());
    }

    [Fact]
    public void A_list_written_before_torrents_existed_loads_as_plain_downloads()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "d.json");
        File.WriteAllText(path, """[{"Url":"https://example.com/a.iso","Path":"C:\\Downloads\\a.iso","Name":"a.iso","Completed":false}]""");

        var record = Assert.Single(new DownloadStore(path).Load());

        Assert.Equal(DownloadKind.Http, record.Kind);
        Assert.Equal(@"C:\Downloads\a.iso", record.Key);
    }

    [Fact]
    public void The_files_chosen_in_a_torrent_survive_a_restart_and_an_old_list_without_them_still_loads()
    {
        var path = Path.Combine(_dir, "d.json");
        var store = new DownloadStore(path);
        store.Save([new DownloadRecord("magnet:?xt=urn:btih:AAAA", @"C:\Down", "Pack", false, DownloadKind.Torrent, [0, 2, 5])]);

        Assert.Equal([0, 2, 5], store.Load()[0].Files);

        File.WriteAllText(path, """[{"Url":"magnet:?xt=urn:btih:BBBB","Path":"C:/Down","Name":"Old","Completed":false,"Kind":1}]""");
        Assert.Null(store.Load()[0].Files);   // null means "all files"
    }

    [Fact]
    public void Torrent_records_round_trip_and_are_keyed_by_their_source_not_their_folder()
    {
        var store = new DownloadStore(Path.Combine(_dir, "d.json"));
        var one = new DownloadRecord("magnet:?xt=urn:btih:AAAA", @"C:\Users\me\Downloads", "Ubuntu", false, DownloadKind.Torrent);
        var two = new DownloadRecord(@"C:\data\torrents\x.torrent", @"C:\Users\me\Downloads", "Movie", false, DownloadKind.Torrent);

        store.Save([one, two]);

        var loaded = store.Load();
        Assert.Equal([one, two], loaded);
        Assert.NotEqual(loaded[0].Key, loaded[1].Key);   // both save into the same folder, so the folder cannot identify them
        Assert.Equal(one.Url, loaded[0].Key);
    }

    [Fact]
    public void Corrupt_file_gives_empty_list()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "d.json");
        File.WriteAllText(path, "[ not json");

        Assert.Empty(new DownloadStore(path).Load());
    }
}
