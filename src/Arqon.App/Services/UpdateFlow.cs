using System.Diagnostics;
using System.Windows;
using Arqon.App.Infrastructure;

namespace Arqon.App.Services;

/// <summary>The "Update" button: install the new version and restart, or say plainly why not and offer the release page.</summary>
public static class UpdateFlow
{
    public static async Task RunAsync(AppServices services)
    {
        if (services.AvailableUpdate is not { } release || services.Updating) return;

        if (release.ExeUrl is null)
        {
            Process.Start(new ProcessStartInfo(release.Url) { UseShellExecute = true });   // an older release without a checksum file
            return;
        }

        var started = await services.InstallUpdateAsync();
        if (started)
        {
            Application.Current.Dispatcher.Invoke(() => ((App)Application.Current).ExitApp());
            return;
        }

        var answer = MessageBox.Show(Loc.Format("S.Upd.Error", services.UpdateError ?? "?"), "Arqon", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Yes) Process.Start(new ProcessStartInfo(release.Url) { UseShellExecute = true });
    }
}
