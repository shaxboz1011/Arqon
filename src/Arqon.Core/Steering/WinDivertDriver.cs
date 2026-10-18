using System.ComponentModel;
using System.Diagnostics;

namespace Arqon.Core.Steering;

/// <summary>
/// WinDivert's driver stays loaded after the last handle is closed, until a reboot or <c>sc stop</c>. While it is loaded an
/// anti-cheat can still see it, so Arqon unloads it whenever it is not needed. Needs administrator rights, like loading it.
/// </summary>
public static class WinDivertDriver
{
    private const string ServiceName = "WinDivert";

    /// <summary>Asks Windows to stop the driver service. Never throws; a missing service or missing rights is fine.</summary>
    public static void Unload()
    {
        try
        {
            using var sc = Process.Start(new ProcessStartInfo("sc.exe", $"stop {ServiceName}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            });
            if (sc is null) return;
            sc.StandardOutput.ReadToEnd();
            sc.WaitForExit(5000);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            // best effort
        }
    }
}
