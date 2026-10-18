namespace Arqon.Core.Steering;

public enum SteeringState { Off, Running, Unavailable, Faulted }

/// <summary>Owns the lifecycle of system mode and turns every failure into a state the UI can show. Never throws.</summary>
public sealed class SteeringService : ISystemModeSwitch
{
    private readonly ISteerer _steerer;
    private readonly SteeringCoordinator _coordinator;
    private readonly NatSteeringEngine _engine;
    private readonly object _gate = new();

    public SteeringService(ISteerer steerer, SteeringCoordinator coordinator, NatSteeringEngine engine)
    {
        _steerer = steerer;
        _coordinator = coordinator;
        _engine = engine;
        _steerer.Faulted += OnFaulted;
    }

    public SteeringState State { get; private set; } = SteeringState.Off;

    public bool IsRunning => State == SteeringState.Running;

    /// <summary>Why steering is unavailable or stopped; null otherwise.</summary>
    public string? Reason { get; private set; }

    public NatSteeringEngine Engine => _engine;

    /// <summary>Whether the driver layer could run right now (driver files present, administrator rights).</summary>
    public bool IsAvailable => _steerer.IsAvailable;

    public string? UnavailableReason => _steerer.UnavailableReason;

    public event Action? StateChanged;

    public bool Start()
    {
        lock (_gate)
        {
            if (State == SteeringState.Running) return true;

            if (!_steerer.IsAvailable)
            {
                Set(SteeringState.Unavailable, _steerer.UnavailableReason);
                return false;
            }

            try
            {
                _steerer.Start();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                SafeStop();
                Set(SteeringState.Faulted, ex.Message);
                return false;
            }
            Set(SteeringState.Running, null);
            return true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (State == SteeringState.Off) return;
            SafeStop();
            Set(SteeringState.Off, null);
        }
    }

    private void OnFaulted(string why)
    {
        lock (_gate)
        {
            SafeStop();
            Set(SteeringState.Faulted, why);
        }
    }

    private void SafeStop()
    {
        try { _steerer.Stop(); }
        catch (Exception) { /* closing must never take the app down; the driver releases the handle with the process anyway */ }
    }

    private void Set(SteeringState state, string? reason)
    {
        State = state;
        Reason = reason;
        StateChanged?.Invoke();
    }
}
