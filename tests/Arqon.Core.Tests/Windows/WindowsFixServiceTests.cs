using Arqon.Core.Windows;

namespace Arqon.Core.Tests.Windows;

public class WindowsFixServiceTests
{
    private sealed class FakePolicy(bool? value) : IWcmPolicy
    {
        public bool? Value { get; private set; } = value;
        public bool? GetMinimizeConnections() => Value;
        public void SetMinimizeConnections(bool v) => Value = v;
    }

    [Fact]
    public void Needs_fix_when_windows_minimises_connections()
    {
        Assert.True(new WindowsFixService(new FakePolicy(true)).NeedsFix);
    }

    [Fact]
    public void Apply_turns_the_policy_off_and_clears_the_need()
    {
        var policy = new FakePolicy(true);
        var service = new WindowsFixService(policy);

        service.Apply();

        Assert.False(policy.Value);
        Assert.False(service.NeedsFix);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public void No_fix_needed_when_policy_is_unset_or_already_off(bool? value)
    {
        Assert.False(new WindowsFixService(new FakePolicy(value)).NeedsFix);
    }

    [Fact]
    public void Describe_names_the_exact_registry_change()
    {
        var text = new WindowsFixService(new FakePolicy(true)).Describe();
        Assert.Contains("fMinimizeConnections", text);
        Assert.Contains("WcmSvc", text);
    }
}
