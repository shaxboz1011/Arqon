using Arqon.Core.Settings;

namespace Arqon.Core.Tests.Settings;

public sealed class SettingsEvolutionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "arqon-evo-" + Guid.NewGuid().ToString("N"));

    public SettingsEvolutionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void A_settings_file_from_an_older_version_gets_the_new_options_switched_on()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "Rules": [], "GameProtection": false, "SystemMode": false, "Language": "en" }""");

        var s = new JsonSettingsStore(path).Load();

        Assert.False(s.GameProtection);
        Assert.Equal("en", s.Language);
        Assert.True(s.CloseToTray);
        Assert.True(s.Notifications);
        Assert.True(s.ClipboardWatch);
    }

    [Fact]
    public void The_new_options_survive_a_save_and_load()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new JsonSettingsStore(path);
        store.Save(ArqonSettings.Default() with { CloseToTray = false, Notifications = false, ClipboardWatch = false });

        var s = store.Load();

        Assert.False(s.CloseToTray);
        Assert.False(s.Notifications);
        Assert.False(s.ClipboardWatch);
    }

    [Fact]
    public void Defaults_have_every_comfort_feature_on()
    {
        var s = ArqonSettings.Default();

        Assert.True(s.CloseToTray && s.Notifications && s.ClipboardWatch);
    }
}
