using System.Globalization;
using System.Text;

namespace Arqon.Core.Diagnostics;

/// <summary>
/// The last crashes, kept in <c>crash.log</c> (by default in <c>%APPDATA%\Arqon</c>), so a bug report can say what went wrong.
/// Each entry starts with a line <c>----- {time}  {source}</c>. It never throws and trims itself: logging a crash must not cause another.
/// </summary>
public sealed class CrashLog
{
    public const string Separator = "----- ";
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss zzz";
    private const long MaxBytes = 512 * 1024;
    private const int MaxEntryChars = 32 * 1024;
    private const int KeepEntries = 20;
    private readonly object _gate = new();

    public CrashLog(string directory) => FilePath = Path.Combine(directory, "crash.log");

    public static string DefaultDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arqon");

    public string FilePath { get; }

    /// <summary>Adds one crash. <paramref name="source"/> says where it was caught, such as "ui", "fatal" or "task".</summary>
    public void Record(Exception ex, string source, DateTimeOffset? at = null)
    {
        var text = ex.ToString();
        if (text.Length > MaxEntryChars) text = text[..MaxEntryChars] + Environment.NewLine + "[cut]";
        var entry = $"{Separator}{(at ?? DateTimeOffset.Now).ToString(TimeFormat, CultureInfo.InvariantCulture)}  {source}{Environment.NewLine}{text}{Environment.NewLine}";

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                    File.WriteAllText(FilePath, string.Concat(ReadEntries().TakeLast(KeepEntries - 1)));
                File.AppendAllText(FilePath, entry);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // nowhere to write: the crash is lost, the program is not
        }
    }

    /// <summary>The newest <paramref name="count"/> entries, oldest first.</summary>
    public IReadOnlyList<string> Recent(int count)
    {
        try
        {
            lock (_gate) return ReadEntries().TakeLast(count).Select(e => e.TrimEnd()).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>When the newest crash happened, or null when none is recorded.</summary>
    public DateTimeOffset? LastAt()
    {
        var last = Recent(1).FirstOrDefault();
        if (last is null || !last.StartsWith(Separator, StringComparison.Ordinal)) return null;
        var header = last.Split('\n', 2)[0][Separator.Length..];
        var cut = header.IndexOf("  ", StringComparison.Ordinal);
        var stamp = (cut >= 0 ? header[..cut] : header).Trim();
        return DateTimeOffset.TryParseExact(stamp, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : null;
    }

    /// <summary>The file cut into entries. Text before the first separator (the older format) counts as one entry.</summary>
    private List<string> ReadEntries()
    {
        var entries = new List<string>();
        if (!File.Exists(FilePath)) return entries;

        var current = new StringBuilder();
        foreach (var line in File.ReadLines(FilePath))
        {
            if (line.StartsWith(Separator, StringComparison.Ordinal) && current.Length > 0)
            {
                Flush();
            }
            current.Append(line).Append(Environment.NewLine);
        }
        Flush();
        return entries;

        void Flush()
        {
            if (!string.IsNullOrWhiteSpace(current.ToString())) entries.Add(current.ToString());
            current.Clear();
        }
    }
}
