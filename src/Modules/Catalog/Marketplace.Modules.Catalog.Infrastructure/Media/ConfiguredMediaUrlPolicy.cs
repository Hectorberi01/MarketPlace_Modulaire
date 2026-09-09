using Marketplace.Modules.Catalog.Application.Abstractions;

namespace Marketplace.Modules.Catalog.Infrastructure.Media;

/// <summary>
/// Politique d'URL de média adossée aux hôtes de stockage RÉELLEMENT configurés.
///
/// Deux sources possibles selon le déploiement : le domaine public du bucket R2, et
/// la base d'URL du service média historique. Les deux sont acceptés quand les deux
/// sont configurés — c'est le seul moyen de ne pas invalider les fiches existantes
/// pendant une bascule d'hébergement.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// AUCUN HÔTE CONFIGURÉ : ON REFUSE TOUT.
///
/// C'est le choix fermé, et il est délibéré. L'alternative — « aucune liste, donc
/// tout passe » — reproduirait exactement le défaut corrigé : une place mal
/// configurée servirait de nouveau des images arbitraires à ses acheteurs, sans que
/// rien ne le signale. Un vendeur qui ne peut pas attacher d'URL se plaint le jour
/// même ; un pixel de traçage sur une fiche produit, personne ne le voit.
///
/// Le téléversement direct, lui, continue de fonctionner : il ne passe pas par cette
/// politique, puisque c'est la plateforme qui produit l'URL.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class ConfiguredMediaUrlPolicy : IMediaUrlPolicy
{
    private readonly IReadOnlyList<string> _hosts;

    public ConfiguredMediaUrlPolicy(CloudflareR2MediaOptions r2, MediaServiceOptions media)
    {
        var hosts = new List<string>(2);
        AddHost(hosts, r2.PublicBaseUrl);
        AddHost(hosts, media.BaseUrl);
        _hosts = hosts;
    }

    public string AllowedDescription =>
        _hosts.Count == 0
            ? "aucun hôte de stockage n'est configuré sur cette instance"
            : string.Join(", ", _hosts);

    public bool IsAllowed(string? url)
    {
        if (_hosts.Count == 0 || string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        // `UriKind.Absolute` écarte d'un coup les chemins relatifs et les URI de
        // données : `data:image/png;base64,…` n'a pas d'hôte, donc n'appartient à
        // aucune liste blanche.
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed))
        {
            return false;
        }

        // HTTPS SEULEMENT. En clair, le contenu servi à l'acheteur peut être
        // remplacé en transit — la liste blanche d'hôtes ne protégerait alors de rien.
        if (parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return _hosts.Any(h => string.Equals(h, parsed.Host, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddHost(List<string> hosts, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)
            || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var parsed))
        {
            return;
        }

        if (!hosts.Contains(parsed.Host, StringComparer.OrdinalIgnoreCase))
        {
            hosts.Add(parsed.Host);
        }
    }
}
