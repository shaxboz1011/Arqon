using System.IO;
using System.Runtime.InteropServices;
using Arqon.App.Infrastructure;
using Arqon.Core.Diagnostics;
using Arqon.Core.Steering;

namespace Arqon.App.Services;

/// <summary>The crash log and the "Copy report" text of the settings page.</summary>
internal static class Diagnostics
{
    public static CrashLog Crashes { get; } = new(CrashLog.DefaultDirectory);

    public static string LogsDirectory => CrashLog.DefaultDirectory;

    public static string BuildReport(AppServices services)
    {
        var state = services.SafeGame.Paused ? "paused for a game"
            : services.Steering.State == SteeringState.Faulted ? $"Faulted ({services.Steering.Reason})"
            : services.Steering.IsAvailable ? services.Steering.State.ToString()
            : $"unavailable ({services.Steering.UnavailableReason})";

        return DiagnosticReport.Build(new DiagnosticInfo(
            AppInfo.Version,
            $"Windows {Environment.OSVersion.Version} {RuntimeInformation.OSArchitecture}",
            Environment.Version.ToString(),
            WinDivertSteerer.IsAdministrator(),
            services.Demo,
            services.Links.GetLinks(),
            services.Monitor.Latest,
            state,
            services.Settings,
            Crashes.Recent(3),
            SteeringTail(40),
            DateTimeOffset.Now,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
    }

    private static IReadOnlyList<string> SteeringTail(int lines)
    {
        try
        {
            return File.Exists(SteeringLog.FilePath) ? File.ReadLines(SteeringLog.FilePath).TakeLast(lines).ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
