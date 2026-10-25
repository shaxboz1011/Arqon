using Arqon.Core.Policy;

namespace Arqon.Core.Settings;

public sealed record ArqonSettings(
    List<AppRule> Rules,
    bool GameProtection,
    bool SystemMode,
    string Language,
    bool CloseToTray = true,
    bool Notifications = true,
    bool ClipboardWatch = true,
    bool TaskbarMeter = true,
    bool SpeedInBytes = false,
    bool SafeGameMode = true,
    int MaxConcurrentDownloads = 3,
    string? DownloadDirectory = null,
    bool SeedTorrents = false,
    double SeedRatio = 1.0,
    bool CheckUpdates = true,
    int DailyLimitMb = 0,
    string? LastNoticedUpdate = null,
    int MeterOffsetDip = 0,
    List<string>? SaverLinks = null,
    bool SaveMobileAtLimit = false,
    bool ScheduleEnabled = false,
    int ScheduleStartHour = 1,
    int ScheduleEndHour = 7,
    bool TorrentPortForwarding = false,
    bool LocalApi = false)
{
    public static ArqonSettings Default() => new([], GameProtection: true, SystemMode: false, Language: "uz");
}
