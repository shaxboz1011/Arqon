namespace Arqon.Core.Torrents;

/// <summary>When a finished torrent has given back enough and seeding may stop.</summary>
public static class SeedPolicy
{
    /// <param name="uploaded">Bytes sent to other peers.</param>
    /// <param name="size">Bytes of the torrent that were downloaded.</param>
    /// <param name="ratio">Stop once <paramref name="uploaded"/> reaches this many times <paramref name="size"/>; 0 (or anything that is not a positive number) means seed until stopped by hand.</param>
    public static bool ReachedTarget(long uploaded, long size, double ratio) =>
        ratio > 0 && size > 0 && uploaded >= size * ratio;
}
