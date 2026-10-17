using Arqon.Core.Links;

namespace Arqon.Core.Monitoring;

public interface IProbe
{
    /// <summary>Round-trip time in ms over this link, or null when the probe failed.</summary>
    Task<double?> ProbeAsync(LinkInfo link, CancellationToken ct);
}
