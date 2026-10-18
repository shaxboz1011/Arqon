using Arqon.Core.Steering;

namespace Arqon.Core.Tests.Steering;

public class SafeGameModeTests
{
    private sealed class FakeSwitch : ISystemModeSwitch
    {
        public bool IsRunning { get; private set; }
        public bool StartSucceeds = true;
        public int Starts, Stops;

        public bool Start()
        {
            Starts++;
            IsRunning = StartSucceeds;
            return StartSucceeds;
        }

        public void Stop()
        {
            Stops++;
            IsRunning = false;
        }
    }

    private sealed class Rig
    {
        public readonly FakeSwitch System = new();
        public bool GameRunning;
        public int Unloads;
        public bool UnloadThrows;
        public readonly SafeGameMode Mode;

        public Rig(bool wantsSystemMode = true, bool systemRunning = true)
        {
            Mode = new SafeGameMode(System, () => GameRunning, () =>
            {
                Unloads++;
                if (UnloadThrows) throw new InvalidOperationException("sc.exe failed");
            })
            { WantsSystemMode = wantsSystemMode };
            if (systemRunning) System.Start();
            System.Starts = 0;
        }
    }

    [Fact]
    public void When_a_game_starts_system_mode_is_stopped_and_the_driver_is_unloaded()
    {
        var rig = new Rig();
        rig.GameRunning = true;

        rig.Mode.Evaluate();

        Assert.True(rig.Mode.Paused);
        Assert.False(rig.System.IsRunning);
        Assert.Equal(1, rig.Unloads);
    }

    [Fact]
    public void When_the_game_ends_system_mode_comes_back()
    {
        var rig = new Rig();
        rig.GameRunning = true;
        rig.Mode.Evaluate();

        rig.GameRunning = false;
        rig.Mode.Evaluate();

        Assert.False(rig.Mode.Paused);
        Assert.True(rig.System.IsRunning);
    }

    [Fact]
    public void Repeated_checks_during_the_game_do_not_stop_or_unload_again()
    {
        var rig = new Rig();
        rig.GameRunning = true;

        rig.Mode.Evaluate();
        rig.Mode.Evaluate();
        rig.Mode.Evaluate();

        Assert.Equal(1, rig.System.Stops);
        Assert.Equal(1, rig.Unloads);
    }

    [Fact]
    public void Nothing_happens_when_the_user_did_not_want_system_mode()
    {
        var rig = new Rig(wantsSystemMode: false, systemRunning: false);
        rig.GameRunning = true;
        rig.Mode.Evaluate();
        rig.GameRunning = false;
        rig.Mode.Evaluate();

        Assert.False(rig.Mode.Paused);
        Assert.Equal(0, rig.System.Starts);
        Assert.Equal(0, rig.Unloads);
    }

    [Fact]
    public void Nothing_happens_when_the_feature_is_switched_off()
    {
        var rig = new Rig();
        rig.Mode.Enabled = false;
        rig.GameRunning = true;

        rig.Mode.Evaluate();

        Assert.False(rig.Mode.Paused);
        Assert.True(rig.System.IsRunning);
    }

    [Fact]
    public void A_game_that_is_already_running_when_arqon_starts_keeps_system_mode_off()
    {
        var rig = new Rig(systemRunning: false);
        rig.GameRunning = true;

        rig.Mode.Evaluate();

        Assert.True(rig.Mode.Paused);
        Assert.Equal(0, rig.System.Starts);
        Assert.Equal(1, rig.Unloads);   // a driver left loaded by an earlier session must go too
    }

    [Fact]
    public void Turning_system_mode_on_during_a_game_does_not_start_it()
    {
        var rig = new Rig(wantsSystemMode: false, systemRunning: false);
        rig.GameRunning = true;
        rig.Mode.Evaluate();

        rig.Mode.WantsSystemMode = true;
        rig.Mode.Evaluate();

        Assert.True(rig.Mode.Paused);
        Assert.False(rig.System.IsRunning);
    }

    [Fact]
    public void A_system_mode_started_by_other_means_during_a_game_is_stopped_again()
    {
        var rig = new Rig();
        rig.GameRunning = true;
        rig.Mode.Evaluate();

        rig.System.Start();
        rig.Mode.Evaluate();

        Assert.False(rig.System.IsRunning);
    }

    [Fact]
    public void Turning_system_mode_off_during_a_pause_ends_the_pause_without_starting_anything()
    {
        var rig = new Rig();
        rig.GameRunning = true;
        rig.Mode.Evaluate();

        rig.Mode.WantsSystemMode = false;
        rig.Mode.Evaluate();
        rig.GameRunning = false;
        rig.Mode.Evaluate();

        Assert.False(rig.Mode.Paused);
        Assert.False(rig.System.IsRunning);
        Assert.Equal(0, rig.System.Starts);
    }

    [Fact]
    public void Switching_the_feature_off_during_a_pause_resumes_system_mode()
    {
        var rig = new Rig();
        rig.GameRunning = true;
        rig.Mode.Evaluate();

        rig.Mode.Enabled = false;
        rig.Mode.Evaluate();

        Assert.False(rig.Mode.Paused);
        Assert.True(rig.System.IsRunning);
    }

    [Fact]
    public void A_failed_resume_does_not_leave_the_pause_stuck()
    {
        var rig = new Rig();
        rig.GameRunning = true;
        rig.Mode.Evaluate();
        rig.System.StartSucceeds = false;

        rig.GameRunning = false;
        rig.Mode.Evaluate();
        rig.Mode.Evaluate();

        Assert.False(rig.Mode.Paused);
        Assert.Equal(1, rig.System.Starts);   // it tried once, it does not loop
    }

    [Fact]
    public void A_driver_that_cannot_be_unloaded_never_breaks_the_check()
    {
        var rig = new Rig { UnloadThrows = true };
        rig.GameRunning = true;

        rig.Mode.Evaluate();

        Assert.True(rig.Mode.Paused);
        Assert.False(rig.System.IsRunning);
    }

    [Fact]
    public void Changed_is_raised_when_the_pause_starts_and_ends_and_only_then()
    {
        var rig = new Rig();
        var changes = 0;
        rig.Mode.Changed += () => changes++;

        rig.Mode.Evaluate();            // nothing yet
        rig.GameRunning = true;
        rig.Mode.Evaluate();            // paused
        rig.Mode.Evaluate();            // still paused
        rig.GameRunning = false;
        rig.Mode.Evaluate();            // resumed

        Assert.Equal(2, changes);
    }

    [Fact]
    public void WouldPause_tells_the_settings_screen_whether_a_game_is_holding_system_mode_back()
    {
        var rig = new Rig(systemRunning: false);

        Assert.False(rig.Mode.WouldPause);
        rig.GameRunning = true;
        Assert.True(rig.Mode.WouldPause);
        rig.Mode.Enabled = false;
        Assert.False(rig.Mode.WouldPause);
    }
}
