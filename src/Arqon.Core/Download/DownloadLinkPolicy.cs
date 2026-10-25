using Arqon.Core.Links;

namespace Arqon.Core.Download;

/// <summary>
/// Which links downloads and torrents may use. A link marked "save data" (say a phone on a small plan) is left alone while
/// another link is up; it is used only when it is the last one. When the daily limit is reached and the user asked for it,
/// every phone link counts as "save data" too. Nothing here ever leaves a download without a link.
/// </summary>
public static class DownloadLinkPolicy
{
    public static IReadOnlyList<LinkInfo> Choose(IReadOnlyList<LinkInfo> links, IReadOnlyCollection<string>? saverLinks, bool saveMobile)
    {
        bool Saved(LinkInfo l) => (saverLinks?.Contains(l.Id, StringComparer.OrdinalIgnoreCase) ?? false) || (saveMobile && l.Kind == LinkKind.Mobile);

        var free = links.Where(l => !Saved(l)).ToList();
        return free.Count > 0 ? free : links;
    }
}
