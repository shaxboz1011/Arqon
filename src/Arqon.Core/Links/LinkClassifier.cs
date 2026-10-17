using System.Net.NetworkInformation;

namespace Arqon.Core.Links;

public static class LinkClassifier
{
    private static readonly string[] OtherMarkers =
        ["bluetooth", "virtual", "vmware", "hyper-v", "vethernet", "tap-", "tap ", "wireguard", "vpn", "loopback", "tunnel", "pseudo"];

    private static readonly string[] MobileMarkers =
        ["rndis", "ndis", "android", "iphone", "apple mobile", "samsung", "huawei", "xiaomi", "mobile", "tether"];

    public static LinkKind Classify(string description, NetworkInterfaceType type)
    {
        if (type is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            return LinkKind.Other;

        var d = description.ToLowerInvariant();

        if (OtherMarkers.Any(d.Contains))
            return LinkKind.Other;

        if (type is NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 || MobileMarkers.Any(d.Contains))
            return LinkKind.Mobile;

        return type switch
        {
            NetworkInterfaceType.Wireless80211 => LinkKind.Wifi,
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet => LinkKind.Ethernet,
            _ => LinkKind.Other,
        };
    }
}
