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
    public void Corrupt_file_gives_empty_list()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "d.json");
        File.WriteAllText(path, "[ not json");

        Assert.Empty(new DownloadStore(path).Load());
    }
}
