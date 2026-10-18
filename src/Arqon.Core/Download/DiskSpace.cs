namespace Arqon.Core.Download;

public static class DiskSpace
{
    /// <summary>Kept free on top of a download, so Windows and other programs do not run out of room because of it.</summary>
    public const long Reserve = 512L * 1024 * 1024;

    public static bool HasRoom(long freeBytes, long neededBytes) => neededBytes <= 0 || freeBytes >= neededBytes + Reserve;

    /// <summary>Free bytes on the drive of <paramref name="path"/>; null when it cannot be told (a network share, a missing drive).</summary>
    public static long? FreeBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\")) return null;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
