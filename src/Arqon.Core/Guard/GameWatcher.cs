using Arqon.Core.Policy;

namespace Arqon.Core.Guard;

/// <summary>Notices which known games are running. Call <see cref="Refresh"/> every few seconds.</summary>
public sealed class GameWatcher(Func<IEnumerable<string>> processNames, Func<RuleEngine> rules)
{
    private IReadOnlyList<string> _running = [];

    public IReadOnlyList<string> RunningGames => _running;

    public bool GameRunning => _running.Count > 0;

    /// <summary>Raised only when the set of running games changes.</summary>
    public event Action? Changed;

    public static IEnumerable<string> CurrentProcessNames() =>
        System.Diagnostics.Process.GetProcesses().Select(p =>
        {
            try { return p.ProcessName + ".exe"; }
            finally { p.Dispose(); }
        });

    public void Refresh()
    {
        IReadOnlyList<string> found;
        try
        {
            var engine = rules();
            found = processNames()
                .Where(n => engine.Resolve(n).Kind == AppKind.Game)
                .Select(GameCatalog.Normalize)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            found = [];   // listing processes can fail (access denied, process exited); treat it as "no game seen"
        }

        if (found.SequenceEqual(_running, StringComparer.OrdinalIgnoreCase)) return;
        _running = found;
        Changed?.Invoke();
    }
}
