using Microsoft.Win32;

namespace Arqon.Core.Windows;

/// <summary>
/// Makes Windows offer Arqon for magnet links and .torrent files (only the current user's registry is touched, no
/// administrator rights). Windows 10 keeps the choice of the default program for itself, so after registering the user
/// may still have to confirm Arqon under Settings, Default apps. A default that someone else owns is never taken over.
/// </summary>
public sealed class ShellAssociations(
    string exePath,
    string classesRoot = @"Software\Classes",
    string appRoot = @"Software\Arqon",
    string registeredApplications = @"Software\RegisteredApplications")
{
    private const string TorrentProgId = "Arqon.Torrent";
    private const string MagnetProgId = "Arqon.Magnet";

    private string Command => $"\"{exePath}\" \"%1\"";

    /// <summary>True when this very copy of Arqon is registered (offered under "Open with" and in Default apps).</summary>
    public bool IsRegistered() => ReadDefault($@"{classesRoot}\{TorrentProgId}\shell\open\command") == Command;

    /// <summary>True when magnet links are opened by this copy of Arqon.</summary>
    public bool OwnsMagnet() => ReadDefault($@"{classesRoot}\magnet\shell\open\command") == Command;

    /// <summary>The command of another program that opens magnet links, or null when nobody else does.</summary>
    public string? OtherMagnetHandler()
    {
        var command = ReadDefault($@"{classesRoot}\magnet\shell\open\command");
        return string.IsNullOrWhiteSpace(command) || command == Command ? null : command;
    }

    public void Register()
    {
        WriteProgId(TorrentProgId, "BitTorrent file");
        WriteProgId(MagnetProgId, "Magnet link");

        // The magnet protocol key itself is only written when nobody else uses it; otherwise the user picks Arqon
        // under Settings, Default apps (it is listed there below).
        if (OtherMagnetHandler() is null)
        {
            using (var magnet = Registry.CurrentUser.CreateSubKey($@"{classesRoot}\magnet"))
            {
                magnet.SetValue(null, "URL:Magnet Link");
                magnet.SetValue("URL Protocol", "");
            }
            SetDefault($@"{classesRoot}\magnet\DefaultIcon", $"\"{exePath}\",0");
            SetDefault($@"{classesRoot}\magnet\shell\open\command", Command);
        }

        // "Open with" lists the program; the default stays whatever the user chose.
        using (var withProgIds = Registry.CurrentUser.CreateSubKey($@"{classesRoot}\.torrent\OpenWithProgids"))
            withProgIds.SetValue(TorrentProgId, Array.Empty<byte>(), RegistryValueKind.None);

        // Appears under Settings, Default apps.
        using (var caps = Registry.CurrentUser.CreateSubKey($@"{appRoot}\Capabilities"))
        {
            caps.SetValue("ApplicationName", "Arqon");
            caps.SetValue("ApplicationDescription", "Uses Wi-Fi and a phone together: faster downloads, torrents and a steady game ping.");
        }
        using (var files = Registry.CurrentUser.CreateSubKey($@"{appRoot}\Capabilities\FileAssociations"))
            files.SetValue(".torrent", TorrentProgId);
        using (var urls = Registry.CurrentUser.CreateSubKey($@"{appRoot}\Capabilities\UrlAssociations"))
            urls.SetValue("magnet", MagnetProgId);
        using (var apps = Registry.CurrentUser.CreateSubKey(registeredApplications))
            apps.SetValue("Arqon", $@"{appRoot}\Capabilities");
    }

    public void Unregister()
    {
        // Only what is Arqon's: a magnet handler that belongs to another program stays.
        if (ReadDefault($@"{classesRoot}\magnet\shell\open\command") == Command)
            Registry.CurrentUser.DeleteSubKeyTree($@"{classesRoot}\magnet", throwOnMissingSubKey: false);

        Registry.CurrentUser.DeleteSubKeyTree($@"{classesRoot}\{TorrentProgId}", throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree($@"{classesRoot}\{MagnetProgId}", throwOnMissingSubKey: false);
        using (var withProgIds = Registry.CurrentUser.OpenSubKey($@"{classesRoot}\.torrent\OpenWithProgids", writable: true))
            withProgIds?.DeleteValue(TorrentProgId, throwOnMissingValue: false);
        Registry.CurrentUser.DeleteSubKeyTree(appRoot, throwOnMissingSubKey: false);
        using (var apps = Registry.CurrentUser.OpenSubKey(registeredApplications, writable: true))
            apps?.DeleteValue("Arqon", throwOnMissingValue: false);
    }

    private void WriteProgId(string progId, string description)
    {
        SetDefault($@"{classesRoot}\{progId}", description);
        SetDefault($@"{classesRoot}\{progId}\DefaultIcon", $"\"{exePath}\",0");
        SetDefault($@"{classesRoot}\{progId}\shell\open\command", Command);
    }

    private static void SetDefault(string path, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path);
        key.SetValue(null, value);
    }

    private static string? ReadDefault(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue(null) as string;
    }

    private static bool KeyExists(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key is not null;
    }
}
