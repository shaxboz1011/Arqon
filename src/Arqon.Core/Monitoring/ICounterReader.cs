using Arqon.Core.Links;

namespace Arqon.Core.Monitoring;

public interface ICounterReader
{
    (long Rx, long Tx) Read(LinkInfo link);
}
