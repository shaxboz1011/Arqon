using MonoTorrent;

namespace Arqon.Core.Torrents;

/// <summary>What the app needs to know about a torrent before the engine is involved: which one it is, and what it is called.</summary>
public sealed record TorrentInfo(string Hash, string Name);

public static class TorrentIdentity
{
    /// <summary>The info hash of a magnet link as lower-case hex (so the same torrent is never added twice), or null when it is not valid.</summary>
    public static string? OfMagnet(string? magnet)
    {
        if (string.IsNullOrWhiteSpace(magnet) || !MagnetLink.TryParse(magnet.Trim(), out var link) || link is null) return null;
        return Hex(link.InfoHashes);
    }

    /// <summary>The identity and name of a .torrent file, or null when the file is missing or is not a torrent.</summary>
    public static TorrentInfo? OfFile(string path)
    {
        try
        {
            return Torrent.TryLoad(path, out var torrent) && torrent is not null ? new TorrentInfo(Hex(torrent.InfoHashes), torrent.Name) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Same as <see cref="OfFile"/> for bytes that were downloaded.</summary>
    public static TorrentInfo? OfBytes(ReadOnlySpan<byte> bytes) =>
        Torrent.TryLoad(bytes, out var torrent) && torrent is not null ? new TorrentInfo(Hex(torrent.InfoHashes), torrent.Name) : null;

    private static string Hex(InfoHashes hashes) => (hashes.V1 ?? hashes.V2!.Truncate()).ToHex().ToLowerInvariant();
}
