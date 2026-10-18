namespace Arqon.Core.Steering;

/// <summary>What <see cref="SafeGameMode"/> needs from system mode: ask whether it runs, start it, stop it.</summary>
public interface ISystemModeSwitch
{
    bool IsRunning { get; }
    bool Start();
    void Stop();
}

/// <summary>
/// Keeps system mode, and with it the packet driver, out of the way while a game runs. Anti-cheat software (VAC, FACEIT,
/// ESEA, Vanguard, ...) looks at loaded drivers, and a network driver is exactly what it distrusts, so the safe thing is
/// that no such driver is loaded while a game is. The user's wish ("system mode on") is kept; only the running state changes.
/// </summary>
public sealed class SafeGameMode(ISystemModeSwitch system, Func<bool> gameRunning, Action unloadDriver)
{
    private readonly object _gate = new();

    /// <summary>The "safe game mode" setting. Switching it off ends a pause on the next <see cref="Evaluate"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The user's preference: system mode switched on in the settings.</summary>
    public bool WantsSystemMode { get; set; }

    /// <summary>True while system mode is held back because a game is running.</summary>
    public bool Paused { get; private set; }

    /// <summary>A game is running and the feature is on, so system mode would be held back right now.</summary>
    public bool WouldPause => Enabled && gameRunning();

    /// <summary>Raised when a pause starts or ends.</summary>
    public event Action? Changed;

    /// <summary>Call whenever the game state or a setting may have changed (it is cheap and idempotent).</summary>
    public void Evaluate()
    {
        var changed = false;
        lock (_gate)
        {
            if (Enabled && WantsSystemMode && gameRunning())
            {
                var stopped = false;
                if (system.IsRunning)
                {
                    system.Stop();   // also catches system mode being started by other means during the pause
                    stopped = true;
                }

                if (!Paused)
                {
                    Paused = true;
                    changed = true;
                    Unload();   // even if nothing ran: an earlier session may have left the driver loaded
                }
                else if (stopped)
                {
                    Unload();
                }
            }
            else if (Paused)
            {
                Paused = false;
                changed = true;
                if (WantsSystemMode) system.Start();   // a failure shows as the usual system mode state; no retry loop
            }
        }

        if (changed) Changed?.Invoke();
    }

    private void Unload()
    {
        try { unloadDriver(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Best effort: the packet handle is already closed, so nothing is diverted either way.
        }
    }
}
