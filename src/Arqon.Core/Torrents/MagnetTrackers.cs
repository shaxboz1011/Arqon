using MonoTorrent;

namespace Arqon.Core.Torrents;

/// <summary>
/// Many magnet links carry no trackers and rely on the DHT, which can be slow or blocked on a network (on the first
/// real test a bare magnet never got its metadata). Public trackers are added to every magnet link, after its own.
/// </summary>
public static class MagnetTrackers
{
    public static readonly IReadOnlyList<string> Public =
    [
        "udp://tracker.opentrackr.org:1337/announce",
        "udp://open.stealth.si:80/announce",
        "udp://exodus.desync.com:6969/announce",
        "udp://tracker.torrent.eu.org:451/announce",
        "udp://explodie.org:6969/announce",
        "udp://open.demonii.com:1337/announce",
        "http://tracker.openbittorrent.com:80/announce",
    ];

    public static MagnetLink Complete(MagnetLink link)
    {
        var trackers = link.AnnounceUrls.ToList();
        foreach (var tracker in Public)
            if (!trackers.Contains(tracker, StringComparer.OrdinalIgnoreCase)) trackers.Add(tracker);

        return new MagnetLink(link.InfoHashes, link.Name, trackers, link.Webseeds, link.Size);
    }
}
