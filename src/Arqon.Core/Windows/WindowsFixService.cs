using Microsoft.Win32;

namespace Arqon.Core.Windows;

public interface IWcmPolicy
{
    /// <summary>Value of "Minimize the number of simultaneous connections", or null when the policy is not set.</summary>
    bool? GetMinimizeConnections();
    void SetMinimizeConnections(bool value);
}

public sealed class RegistryWcmPolicy : IWcmPolicy
{
    public const string KeyPath = @"SOFTWARE\Policies\Microsoft\Windows\WcmSvc\GroupPolicy";
    public const string ValueName = "fMinimizeConnections";

    public bool? GetMinimizeConnections()
    {
        using var key = Registry.LocalMachine.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) is int v ? v != 0 : null;
    }

    public void SetMinimizeConnections(bool value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(KeyPath, writable: true);
        key.SetValue(ValueName, value ? 1 : 0, RegistryValueKind.DWord);
    }
}

/// <summary>
/// Windows can switch off Wi-Fi (or the phone) as soon as another network connects. This policy stops that,
/// so both links stay up at the same time. The change is explained to the user before it is applied.
/// </summary>
public sealed class WindowsFixService(IWcmPolicy policy)
{
    public bool NeedsFix => policy.GetMinimizeConnections() == true;

    public void Apply() => policy.SetMinimizeConnections(false);

    public string Describe() =>
        @"Sets HKLM\" + RegistryWcmPolicy.KeyPath + @"\" + RegistryWcmPolicy.ValueName + " = 0 " +
        "(\"Minimize the number of simultaneous connections to the Internet\" = Disabled) so Windows keeps " +
        "every connection active. Needs administrator rights and can be reverted by deleting that value.";
}
