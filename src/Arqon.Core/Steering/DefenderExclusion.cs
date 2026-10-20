using System.ComponentModel;
using System.Diagnostics;

namespace Arqon.Core.Steering;

/// <summary>
/// Windows Defender treats the WinDivert packet driver as suspicious (it reports it as "Misleading:Win32/Malgent" or as a hack tool)
/// because any program that can divert packets could misuse it, and it removes the files. Telling Defender to leave the one folder
/// Arqon keeps the driver in alone is what makes system mode work on a computer where it was being deleted. It needs administrator
/// rights and only ever names that one folder.
/// </summary>
public static class DefenderExclusion
{
    /// <summary>Adds <paramref name="folder"/> to Defender's exclusions. Returns false when it could not (no Defender, another antivirus, no rights).</summary>
    public static bool TryAdd(string folder)
    {
        try
        {
            // the folder goes in as an argument-free environment variable, never spliced into the script text
            var info = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"Add-MpPreference -ExclusionPath $env:ARQON_EXCLUDE\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.Environment["ARQON_EXCLUDE"] = folder;
            using var process = Process.Start(info);
            if (process is null) return false;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            return process.WaitForExit(30_000) && process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
