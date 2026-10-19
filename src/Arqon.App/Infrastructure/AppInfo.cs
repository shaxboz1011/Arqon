using System.Reflection;

namespace Arqon.App.Infrastructure;

/// <summary>What the running program knows about itself.</summary>
public static class AppInfo
{
    /// <summary>The version of this build, such as "0.1.3" (the build's commit suffix, if any, is dropped).</summary>
    public static string Version { get; } = Read();

    private static string Read()
    {
        var info = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                   ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
                   ?? "0.0.0";
        var cut = info.IndexOf('+');
        return cut >= 0 ? info[..cut] : info;
    }
}
