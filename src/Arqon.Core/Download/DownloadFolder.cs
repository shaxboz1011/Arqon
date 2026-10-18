using System.Runtime.InteropServices;

namespace Arqon.Core.Download;

/// <summary>Where downloads go: the folder the user chose, or by default the Downloads folder of Windows.</summary>
public static class DownloadFolder
{
    public static bool IsCustom(string? chosen) => !string.IsNullOrWhiteSpace(chosen);

    public static string Resolve(string? chosen, string windowsDownloads) =>
        IsCustom(chosen) ? chosen!.Trim() : windowsDownloads;

    /// <summary>The Downloads folder as Windows knows it today (it can be moved, so the user profile path is only a fallback).</summary>
    public static string Windows() => KnownFolders.Downloads();
}

public static class KnownFolders
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);

    public static string Downloads()
    {
        try
        {
            var id = DownloadsId;
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var pointer) == 0)
            {
                try
                {
                    if (Marshal.PtrToStringUni(pointer) is { Length: > 0 } path) return path;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pointer);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or COMException)
        {
            // fall through to the profile path
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
}
