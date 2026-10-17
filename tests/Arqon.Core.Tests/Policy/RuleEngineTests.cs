using Arqon.Core.Policy;
using Arqon.Core.Settings;

namespace Arqon.Core.Tests.Policy;

public class RuleEngineTests
{
    private static RuleEngine Engine(params AppRule[] rules) =>
        new(ArqonSettings.Default() with { Rules = rules.ToList() });

    [Fact]
    public void Known_game_is_detected_without_a_rule()
    {
        var rule = Engine().Resolve("Valorant.exe");
        Assert.Equal(AppKind.Game, rule.Kind);
        Assert.Equal(LinkChoice.Auto, rule.Choice);
    }

    [Theory]
    [InlineData("VALORANT-Win64-Shipping.exe")]
    [InlineData("cs2.exe")]
    [InlineData("C:\\Games\\Dota 2\\dota2.exe")]
    public void Game_catalogue_matches_case_insensitively_and_ignores_directories(string exe)
    {
        Assert.True(GameCatalog.IsGame(exe));
    }

    [Fact]
    public void Explicit_rule_overrides_the_catalogue()
    {
        var engine = Engine(new AppRule("valorant.exe", AppKind.Normal, LinkChoice.Only, "phone", null));
        var rule = engine.Resolve("Valorant.exe");
        Assert.Equal(LinkChoice.Only, rule.Choice);
        Assert.Equal("phone", rule.LinkId);
    }

    [Fact]
    public void Unknown_app_uses_both_links()
    {
        var rule = Engine().Resolve("chrome.exe");
        Assert.Equal(AppKind.Normal, rule.Kind);
        Assert.Equal(LinkChoice.Both, rule.Choice);
        Assert.Null(rule.LinkId);
    }

    [Fact]
    public void Exe_name_without_extension_still_matches_a_rule()
    {
        var engine = Engine(new AppRule("steam.exe", AppKind.Downloader, LinkChoice.Only, "phone", 10_000_000));
        Assert.Equal(AppKind.Downloader, engine.Resolve("Steam").Kind);
    }
}
