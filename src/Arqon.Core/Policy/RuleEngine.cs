using Arqon.Core.Settings;

namespace Arqon.Core.Policy;

public sealed class RuleEngine(ArqonSettings settings)
{
    /// <summary>Explicit rule first, then the built-in game catalogue, then "use both links".</summary>
    public AppRule Resolve(string exeName)
    {
        var name = GameCatalog.Normalize(exeName);

        var explicitRule = settings.Rules.FirstOrDefault(r =>
            string.Equals(GameCatalog.Normalize(r.ExeName), name, StringComparison.OrdinalIgnoreCase));
        if (explicitRule is not null) return explicitRule;

        return GameCatalog.IsGame(name)
            ? new AppRule(name, AppKind.Game, LinkChoice.Auto, null, null)
            : new AppRule(name, AppKind.Normal, LinkChoice.Both, null, null);
    }
}
