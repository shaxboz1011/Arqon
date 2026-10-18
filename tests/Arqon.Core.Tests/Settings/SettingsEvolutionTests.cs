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
        Assert.True(s.TaskbarMeter);
        Assert.False(s.SpeedInBytes);   // an older file keeps showing megabits, as before
        Assert.True(s.SafeGameMode);    // the safe choice is the default for files written before it existed
    }

    [Fact]
    public void An_older_file_gets_three_parallel_downloads_and_the_choice_survives_a_save_and_load()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "Rules": [], "GameProtection": true, "SystemMode": false, "Language": "uz" }""");
        var store = new JsonSettingsStore(path);

        Assert.Equal(3, store.Load().MaxConcurrentDownloads);

        store.Save(ArqonSettings.Default() with { MaxConcurrentDownloads = 5 });
        Assert.Equal(5, store.Load().MaxConcurrentDownloads);
    }

    [Fact]
    public void The_download_folder_is_automatic_until_the_user_picks_one()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{ "Rules": [], "GameProtection": true, "SystemMode": false, "Language": "uz" }""");
        var store = new JsonSettingsStore(path);

        Assert.Null(store.Load().DownloadDirectory);

        store.Save(ArqonSettings.Default() with { DownloadDirectory = @"D:\Torrents" });
        Assert.Equal(@"D:\Torrents", store.Load().DownloadDirectory);

        store.Save(ArqonSettings.Default() with { DownloadDirectory = null });
        Assert.Null(store.Load().DownloadDirectory);   // back to automatic
    }

    [Fact]
    public void The_safe_game_mode_choice_survives_a_save_and_load()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new JsonSettingsStore(path);
        store.Save(ArqonSettings.Default() with { SafeGameMode = false });

        Assert.False(store.Load().SafeGameMode);
    }

    [Fact]
    public void The_speed_unit_survives_a_save_and_load()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new JsonSettingsStore(path);
        store.Save(ArqonSettings.Default() with { SpeedInBytes = true });

        Assert.True(store.Load().SpeedInBytes);
    }

    [Fact]
    public void The_new_options_survive_a_save_and_load()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new JsonSettingsStore(path);
        store.Save(ArqonSettings.Default() with { CloseToTray = false, Notifications = false, ClipboardWatch = false, TaskbarMeter = false });

        var s = store.Load();

        Assert.False(s.CloseToTray);
        Assert.False(s.Notifications);
        Assert.False(s.ClipboardWatch);
        Assert.False(s.TaskbarMeter);
    }

    [Fact]
    public void Defaults_have_every_comfort_feature_on()
    {
        var s = ArqonSettings.Default();

        Assert.True(s.CloseToTray && s.Notifications && s.ClipboardWatch && s.TaskbarMeter);
    }
}
