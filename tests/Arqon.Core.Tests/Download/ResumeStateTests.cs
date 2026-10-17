using Arqon.Core.Download;

namespace Arqon.Core.Tests.Download;

public sealed class ResumeStateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arqon-resume-" + Guid.NewGuid().ToString("N"));
    private static readonly Uri Url = new("https://example.com/a.bin");

    public ResumeStateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private string Write(string json)
    {
        var path = Path.Combine(_dir, "a.arqstate");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Round_trips_with_a_validator()
    {
        var path = Path.Combine(_dir, "a.arqstate");
        new ResumeState(Url.ToString(), 100, 10, [[0, 9]], "\"v1\"").Save(path);

        var loaded = ResumeState.TryLoad(path, Url, 100, 10, "\"v1\"");

        Assert.NotNull(loaded);
        Assert.Equal([new ByteRange(0, 9)], loaded!.ToRanges());
    }

    [Fact]
    public void A_different_validator_means_the_remote_file_changed()
    {
        var path = Path.Combine(_dir, "a.arqstate");
        new ResumeState(Url.ToString(), 100, 10, [[0, 9]], "\"v1\"").Save(path);

        Assert.Null(ResumeState.TryLoad(path, Url, 100, 10, "\"v2\""));
    }

    [Theory]
    [InlineData("""{ "Url": "https://example.com/a.bin", "Length": 100, "ChunkSize": 10, "Completed": null }""")]
    [InlineData("""{ "Url": "https://example.com/a.bin", "Length": 100, "ChunkSize": 10, "Completed": [[0]] }""")]
    [InlineData("""{ "Url": "https://example.com/a.bin", "Length": 100, "ChunkSize": 10, "Completed": [null] }""")]
    public void Incomplete_state_is_ignored_instead_of_throwing(string json)
    {
        Assert.Null(ResumeState.TryLoad(Write(json), Url, 100, 10, null));
    }
}
