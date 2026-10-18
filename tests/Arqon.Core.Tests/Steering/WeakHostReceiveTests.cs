using Arqon.Core.Steering;

namespace Arqon.Core.Tests.Steering;

public class WeakHostReceiveTests
{
    private sealed class Fake
    {
        public readonly Dictionary<uint, bool> State = new();
        public readonly List<string> Calls = [];

        public WeakHostScope Scope() => new(
            i => State.TryGetValue(i, out var v) ? v : null,
            (i, v) => { State[i] = v; Calls.Add($"{i}={v}"); });
    }

    [Fact]
    public void Enabling_turns_it_on_only_where_it_was_off()
    {
        var f = new Fake { State = { [10] = false, [20] = true } };
        using var scope = f.Scope();

        scope.Enable([10, 20]);

        Assert.True(f.State[10]);
        Assert.Equal(["10=True"], f.Calls);
    }

    [Fact]
    public void Dispose_restores_only_what_it_changed()
    {
        var f = new Fake { State = { [10] = false, [20] = true } };
        var scope = f.Scope();
        scope.Enable([10, 20]);

        scope.Dispose();

        Assert.False(f.State[10]);
        Assert.True(f.State[20]);   // was already on before us: left alone
    }

    [Fact]
    public void Enabling_twice_changes_nothing_the_second_time_and_still_restores_the_original()
    {
        var f = new Fake { State = { [10] = false } };
        var scope = f.Scope();
        scope.Enable([10]);
        scope.Enable([10]);

        Assert.Equal(["10=True"], f.Calls);
        scope.Dispose();
        Assert.False(f.State[10]);
    }

    [Fact]
    public void An_interface_that_cannot_be_read_is_not_touched()
    {
        var f = new Fake();
        using var scope = f.Scope();

        scope.Enable([99]);

        Assert.Empty(f.Calls);
    }

    [Fact]
    public void Dispose_is_safe_to_call_twice()
    {
        var f = new Fake { State = { [10] = false } };
        var scope = f.Scope();
        scope.Enable([10]);
        scope.Dispose();
        scope.Dispose();

        Assert.Equal(["10=True", "10=False"], f.Calls);
    }

    [Fact]
    public void The_real_interface_table_can_be_read()
    {
        // Loopback always exists. A wrong native struct layout would make this call fail, so it guards the P/Invoke.
        Assert.NotNull(WeakHostReceive.Get(1));
    }
}
