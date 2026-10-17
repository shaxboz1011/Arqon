using System.Net.NetworkInformation;
using Arqon.Core.Links;

namespace Arqon.Core.Tests.Links;

public class LinkClassifierTests
{
    [Theory]
    [InlineData("Intel(R) Wi-Fi 6E AX211 160MHz", NetworkInterfaceType.Wireless80211, LinkKind.Wifi)]
    [InlineData("Remote NDIS based Internet Sharing Device", NetworkInterfaceType.Ethernet, LinkKind.Mobile)]
    [InlineData("Samsung Mobile USB Remote NDIS Network Device", NetworkInterfaceType.Ethernet, LinkKind.Mobile)]
    [InlineData("Apple Mobile Device Ethernet", NetworkInterfaceType.Ethernet, LinkKind.Mobile)]
    [InlineData("Intel(R) Ethernet Connection (16) I219-LM", NetworkInterfaceType.Ethernet, LinkKind.Ethernet)]
    [InlineData("Bluetooth Device (Personal Area Network)", NetworkInterfaceType.Ethernet, LinkKind.Other)]
    [InlineData("Hyper-V Virtual Ethernet Adapter", NetworkInterfaceType.Ethernet, LinkKind.Other)]
    [InlineData("WireGuard Tunnel", NetworkInterfaceType.Tunnel, LinkKind.Other)]
    [InlineData("Software Loopback Interface 1", NetworkInterfaceType.Loopback, LinkKind.Other)]
    public void Classify_returns_expected_kind(string description, NetworkInterfaceType type, LinkKind expected)
    {
        Assert.Equal(expected, LinkClassifier.Classify(description, type));
    }
}
