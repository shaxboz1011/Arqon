using System.Net;

namespace Arqon.Core.Links;

public enum LinkKind { Wifi, Ethernet, Mobile, Other }

public sealed record LinkInfo(
    string Id,
    string Name,
    string Description,
    IPAddress LocalAddress,
    IPAddress? Gateway,
    LinkKind Kind,
    long SpeedBps);
