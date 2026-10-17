using Arqon.Core.Policy;
using Arqon.Core.Settings;

namespace Arqon.Core.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arqon-settings-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var s = new JsonSettingsStore(Path.Combine(_dir, "settings.json")).Load();
        Assert.True(s.GameProtection);
        Assert.False(s.SystemMode);
        Assert.Equal("uz", s.Language);
        Assert.Empty(s.Rules);
    }

    [Fact]
    public void Settings_round_trip_including_enums()
    {
        var store = new JsonSettingsStore(Path.Combine(_dir, "nested", "settings.json"));
        var original = ArqonSettings.Default() with
        {
            Language = "en",
            SystemMode = true,
            Rules = [new AppRule("steam.exe", AppKind.Downloader, LinkChoice.Only, "phone", 10_000_000)],
        };

        store.Save(original);
        var loaded = store.Load();

        Assert.Equal("en", loaded.Language);
        Assert.True(loaded.SystemMode);
        var rule = Assert.Single(loaded.Rules);
        Assert.Equal(new AppRule("steam.exe", AppKind.Downloader, LinkChoice.Only, "phone", 10_000_000), rule);
    }

    [Fact]
    public void Corrupt_file_gives_defaults_and_does_not_throw()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ this is not json");

        var s = new JsonSettingsStore(path).Load();

        Assert.True(s.GameProtection);
        Assert.Equal("uz", s.Language);
    }
}
