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
    bool SpeedInBytes = false)
{
    public static ArqonSettings Default() => new([], GameProtection: true, SystemMode: false, Language: "uz");
}
