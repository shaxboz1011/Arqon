namespace Arqon.Core.Policy;

public static class GameCatalog
{
    private static readonly HashSet<string> Games = new(StringComparer.OrdinalIgnoreCase)
    {
        "valorant.exe", "valorant-win64-shipping.exe",
        "cs2.exe", "csgo.exe",
        "dota2.exe",
        "fortniteclient-win64-shipping.exe",
        "leagueclient.exe", "league of legends.exe",
        "tslgame.exe", "pubg.exe",
        "r5apex.exe", "r5apex_dx12.exe",
        "overwatch.exe",
        "gta5.exe", "gtav.exe",
        "rocketleague.exe",
        "minecraft.windows.exe",
        "rainbowsix.exe", "rainbowsix_vulkan.exe",
        "destiny2.exe",
        "cod.exe", "modernwarfare.exe",
        "wow.exe", "wowclassic.exe",
        "eafc24.exe", "fc25.exe", "fifa23.exe",
        "brawlhalla.exe", "apexlegends.exe",
    };

    public static bool IsGame(string exeName) => Games.Contains(Normalize(exeName));

    /// <summary>File name only, with an .exe extension.</summary>
    public static string Normalize(string exeName)
    {
        var name = Path.GetFileName(exeName.Trim().Replace('/', '\\'));
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
    }
}
