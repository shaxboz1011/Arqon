using System.IO;

namespace Arqon.App.Services;

/// <summary>
/// Small append-only text log of system-mode events (<c>%APPDATA%\Arqon\steering.log</c>), so a run on real hardware can be
/// understood afterwards. It never throws and trims itself, because logging must not be able to break networking.
/// </summary>
internal static class SteeringLog
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();
    private static readonly string Path_ = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arqon", "steering.log");

    public static string FilePath => Path_;

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path_)!);
                if (File.Exists(Path_) && new FileInfo(Path_).Length > MaxBytes)
                    File.WriteAllLines(Path_, File.ReadLines(Path_).TakeLast(500));
                File.AppendAllText(Path_, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // losing a log line is fine
        }
    }
}
