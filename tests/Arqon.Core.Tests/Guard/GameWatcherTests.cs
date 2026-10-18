using Arqon.Core.Guard;
using Arqon.Core.Policy;
using Arqon.Core.Settings;

namespace Arqon.Core.Tests.Guard;

public class GameWatcherTests
{
    private static GameWatcher Make(Func<IEnumerable<string>> processes, params AppRule[] rules) =>
        new(processes, () => new RuleEngine(ArqonSettings.Default() with { Rules = rules.ToList() }));

    [Fact]
    public void Recognises_catalogue_games_among_running_processes()
    {
        var w = Make(() => ["explorer.exe", "VALORANT-Win64-Shipping.exe", "chrome.exe"]);

        w.Refresh();

        Assert.True(w.GameRunning);
        Assert.Contains(w.RunningGames, g => g.Contains("valorant", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Reports_no_game_for_ordinary_programs()
    {
        var w = Make(() => ["explorer.exe", "chrome.exe", "telegram.exe"]);

        w.Refresh();

        Assert.False(w.GameRunning);
        Assert.Empty(w.RunningGames);
    }

    [Fact]
    public void Honours_apps_the_user_marked_as_games()
    {
        var w = Make(() => ["mygame.exe"], new AppRule("mygame.exe", AppKind.Game, LinkChoice.Auto, null, null));

        w.Refresh();

        Assert.True(w.GameRunning);
    }

    [Fact]
    public void Announces_changes_only_when_the_set_of_games_changes()
    {
        var list = new List<string> { "chrome.exe" };
        var w = Make(() => list.ToList());
        var changes = 0;
        w.Changed += () => changes++;

        w.Refresh();
        w.Refresh();
        Assert.Equal(0, changes);

        list.Add("dota2.exe");
        w.Refresh();
        w.Refresh();
        Assert.Equal(1, changes);

        list.Remove("dota2.exe");
        w.Refresh();
        Assert.Equal(2, changes);
    }

    [Fact]
    public void A_failing_process_list_is_survived()
    {
        var w = Make(() => throw new InvalidOperationException("access denied"));

        w.Refresh();

        Assert.False(w.GameRunning);
    }
}
