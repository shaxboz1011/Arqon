using System.Net;
using Arqon.Core.Links;
using Arqon.Core.Monitoring;
using Arqon.Core.Policy;
using Arqon.Core.Settings;
using Arqon.Core.Steering;

namespace Arqon.Core.Tests.Steering;

public class SteeringServiceTests
{
    private sealed class FakeSteerer : ISteerer
    {
        public bool Available = true;
        public string? Reason;
        public bool StartThrows;
        public int Starts, Stops;
        public bool IsAvailable => Available;
        public string? UnavailableReason => Reason;
        public void Start() { if (StartThrows) throw new InvalidOperationException("boom"); Starts++; }
        public void Stop() => Stops++;
        public event Action<string>? Faulted;
        public void Fault(string why) => Faulted?.Invoke(why);
    }

    private sealed class NoOwners : IProcessResolver
    {
        public ProcessInfo? Resolve(byte protocol, ushort localPort) => null;
    }

    private static (SteeringService Service, FakeSteerer Steerer) Make()
    {
        var steerer = new FakeSteerer();
        var coordinator = new SteeringCoordinator(steerer, new FlowTable(() => DateTimeOffset.UtcNow),
            new RuleEngine(ArqonSettings.Default()), () => []);
        var engine = new NatSteeringEngine(coordinator, new NoOwners(), () => [], () => null, 1, () => DateTimeOffset.UtcNow);
        return (new SteeringService(steerer, coordinator, engine), steerer);
    }

    [Fact]
    public void Starts_off()
    {
        Assert.Equal(SteeringState.Off, Make().Service.State);
    }

    [Fact]
    public void Start_runs_the_steerer()
    {
        var (service, steerer) = Make();

        Assert.True(service.Start());

        Assert.Equal(SteeringState.Running, service.State);
        Assert.Equal(1, steerer.Starts);
    }

    [Fact]
    public void Start_twice_starts_the_steerer_once()
    {
        var (service, steerer) = Make();
        service.Start();
        service.Start();

        Assert.Equal(1, steerer.Starts);
    }

    [Fact]
    public void Without_the_driver_it_reports_why_and_does_not_start()
    {
        var (service, steerer) = Make();
        steerer.Available = false;
        steerer.Reason = "driver-missing";

        Assert.False(service.Start());

        Assert.Equal(SteeringState.Unavailable, service.State);
        Assert.Equal("driver-missing", service.Reason);
        Assert.Equal(0, steerer.Starts);
    }

    [Fact]
    public void A_start_failure_is_reported_not_thrown()
    {
        var (service, steerer) = Make();
        steerer.StartThrows = true;

        Assert.False(service.Start());

        Assert.Equal(SteeringState.Faulted, service.State);
        Assert.Contains("boom", service.Reason);
    }

    [Fact]
    public void A_fault_while_running_stops_steering_and_keeps_the_reason()
    {
        var (service, steerer) = Make();
        service.Start();

        steerer.Fault("driver closed");

        Assert.Equal(SteeringState.Faulted, service.State);
        Assert.Equal("driver closed", service.Reason);
        Assert.True(steerer.Stops >= 1);
    }

    [Fact]
    public void Stop_returns_to_off_and_releases_the_driver()
    {
        var (service, steerer) = Make();
        service.Start();

        service.Stop();

        Assert.Equal(SteeringState.Off, service.State);
        Assert.Equal(1, steerer.Stops);
    }

    [Fact]
    public void It_can_be_started_again_after_a_fault()
    {
        var (service, steerer) = Make();
        service.Start();
        steerer.Fault("x");

        Assert.True(service.Start());
        Assert.Equal(SteeringState.Running, service.State);
    }

    [Fact]
    public void State_changes_are_announced()
    {
        var (service, steerer) = Make();
        var seen = new List<SteeringState>();
        service.StateChanged += () => seen.Add(service.State);

        service.Start();
        steerer.Fault("x");
        service.Stop();

        Assert.Equal([SteeringState.Running, SteeringState.Faulted, SteeringState.Off], seen);
    }
}
