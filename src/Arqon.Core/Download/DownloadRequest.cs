namespace Arqon.Core.Download;

public sealed record DownloadRequest(
    Uri Url,
    string DestinationPath,
    long ChunkSize = 4 * 1024 * 1024,
    long? SpeedLimitBps = null,
    LinkThrottle? Throttle = null);

/// <summary>
/// Progress snapshot. PerLinkBytes counts bytes of finished chunks per link and always sums to the finished total;
/// BytesPerSecond is the smoothed wire speed across all links.
/// </summary>
public sealed record DownloadProgress(
    long Total,
    long Done,
    IReadOnlyDictionary<string, long> PerLinkBytes,
    double BytesPerSecond);
