namespace Arqon.Core.Links;

public interface ILinkProvider
{
    IReadOnlyList<LinkInfo> GetLinks();
}
