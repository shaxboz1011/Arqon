using System.Reflection;
using System.Runtime.InteropServices;

namespace Arqon.Core.Steering;

/// <summary>
/// Finds the WinDivert driver files. They may sit next to <c>Arqon.exe</c> (the manual way) or in the user's own folder
/// <c>%APPDATA%\Arqon\driver</c>, where the in-app setup puts them (that folder needs no administrator rights to write to).
/// </summary>
public static class WinDivertLocator
{
    public const string DllName = "WinDivert.dll";

    public static string SysName => Environment.Is64BitProcess ? "WinDivert64.sys" : "WinDivert32.sys";

    /// <summary>The folder the in-app setup installs into.</summary>
    public static string UserDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Arqon", "driver");

    public static bool HasDriver(string directory) =>
        File.Exists(Path.Combine(directory, DllName)) && File.Exists(Path.Combine(directory, SysName));

    /// <summary>The first folder, the program's own or the user's, that holds both files; null when neither does.</summary>
    public static string? Find(string programDirectory)
    {
        if (HasDriver(programDirectory)) return programDirectory;
        return HasDriver(UserDirectory) ? UserDirectory : null;
    }

    private static int _registered;

    /// <summary>Lets the program load WinDivert.dll from the user's folder when it is not next to the exe. Safe to call many times.</summary>
    internal static void RegisterResolver()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
            NativeLibrary.SetDllImportResolver(typeof(WinDivertLocator).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, DllName, StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;
        var folder = Find(AppContext.BaseDirectory);
        if (folder is null || string.Equals(Path.TrimEndingDirectorySeparator(folder), Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;   // next to the exe (or missing): the normal search does it
        return NativeLibrary.TryLoad(Path.Combine(folder, DllName), out var handle) ? handle : IntPtr.Zero;
    }
}
