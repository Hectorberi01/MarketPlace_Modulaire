using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Partners;

namespace Marketplace.Api.Extensions;

/// <summary>
/// Le partenaire authentifié pour la requête en cours. Déposé par
/// <see cref="PartnerApiKeyFilter"/>, lu par les endpoints.
/// </summary>
public sealed record AuthenticatedPartner(Guid Id, string Name);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// AUTHENTIFICATION PAR CLÉ D'API — L'ENTRÉE DE L'API PUBLIQUE.
///
/// POURQUOI UN FILTRE D'ENDPOINT ET NON UN SCHÉMA D'AUTHENTIFICATION ASP.NET
///
/// Un <c>AuthenticationHandler</c> produirait un <c>ClaimsPrincipal</c>, et
/// brancherait donc les partenaires sur la même mécanique que les utilisateurs —
/// jetons JWT, rôles, politiques d'autorisation. Ce sont deux populations qui
/// n'ont rien en commun : un partenaire n'a pas de rôle, ne se connecte pas, ne
/// rafraîchit pas de jeton, et ne doit surtout PAS pouvoir atteindre les routes
/// utilisateur si un jour quelqu'un pose une politique trop large.
///
/// Les tenir séparés rend cette confusion impossible par construction.
///
/// CE QUE CE FILTRE FAIT, ET CE QU'IL NE FAIT PAS
///
/// Il établit QUI appelle. Rien d'autre. Le statut du partenaire et son quota
/// sont vérifiés dans la couche Application, où ils valent quel que soit le
/// transport — les mettre ici les rendrait contournables par tout autre chemin
/// d'entrée.
///
/// ⚠️ LA CLÉ NE DOIT JAMAIS ÊTRE JOURNALISÉE.
///
/// Seul le PRÉFIXE est manipulable en clair. Il est conçu pour cela : il désigne
/// la clé sans permettre de la reconstituer. Toute journalisation ajoutée ici doit
/// s'en tenir au préfixe — une clé dans un journal est une clé compromise, et les
/// journaux se conservent bien plus longtemps que les clés.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class PartnerApiKeyFilter : IEndpointFilter
{
    /// <summary>En-tête attendu. Le nom est celui qu'attendent les intégrateurs habitués aux PSP.</summary>
    public const string HeaderName = "X-API-Key";

    private readonly IPartnerRepository _partners;
    private readonly IDeliveryUnitOfWork _unitOfWork;
    private readonly ILogger<PartnerApiKeyFilter> _logger;

    public PartnerApiKeyFilter(
        IPartnerRepository partners,
        IDeliveryUnitOfWork unitOfWork,
        ILogger<PartnerApiKeyFilter> logger)
    {
        _partners = partners;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        var presented = ReadKey(http.Request);
        if (presented is null)
        {
            return Unauthorized("Clé d'API absente.",
                $"Présentez votre clé dans l'en-tête « {HeaderName} », ou en « Authorization: Bearer ».");
        }

        // Le préfixe est extrait de la clé PRÉSENTÉE, sans toucher la base : une
        // clé mal formée est rejetée avant toute requête, ce qui évite qu'un
        // flot de requêtes bidons se traduise en charge sur PostgreSQL.
        var prefix = PartnerApiKey.ExtractPrefix(presented);
        if (prefix is null)
        {
            return Unauthorized("Clé d'API invalide.", "La forme attendue est « hba_<env>_<préfixe>_<secret> ».");
        }

        var partner = await _partners.FindByApiKeyPrefixAsync(prefix, http.RequestAborted);
        var key = partner?.FindActiveKey(prefix);

        // ─────────────────────────────────────────────────────────────────────
        // UN SEUL MESSAGE POUR TOUS LES ÉCHECS D'AUTHENTIFICATION.
        //
        // Distinguer « préfixe inconnu » de « secret incorrect » dirait à qui
        // essaie s'il tient une clé réelle dont il ignore la fin, ou rien du
        // tout. C'est la différence entre une attaque désespérée et une attaque
        // orientée.
        //
        // Matches() compare à temps constant : voir PartnerApiKey.
        // ─────────────────────────────────────────────────────────────────────
        if (partner is null || key is null || !key.Matches(presented))
        {
            // Le préfixe, LUI, se journalise : c'est sa raison d'être. Il permet
            // de reconnaître une clé révoquée encore déployée chez un partenaire,
            // ce qui est la cause la plus fréquente d'un 401 en production.
            _logger.LogWarning(
                "API partenaire : clé refusée (préfixe {Prefix}, {Path}).", prefix, http.Request.Path);

            return Unauthorized("Clé d'API invalide.", "Vérifiez la clé, ou son éventuelle révocation.");
        }

        // Marque l'usage — au plus une écriture par heure et par clé. Sert à
        // repérer les clés dormantes, qui sont la première porte d'entrée d'une
        // fuite ancienne. L'échec de cette écriture ne doit jamais faire échouer
        // l'appel du partenaire : c'est de la télémétrie, pas de la sécurité.
        try
        {
            if (key.TouchIfStale())
            {
                await _unitOfWork.SaveChangesAsync(http.RequestAborted);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "API partenaire : horodatage d'usage non enregistré pour la clé {Prefix}.", prefix);
        }

        http.Items[nameof(AuthenticatedPartner)] = new AuthenticatedPartner(partner.Id.Value, partner.Name);

        return await next(context);
    }

    /// <summary>
    /// Le partenaire de la requête. Lève si le filtre n'a pas tourné — ce n'est
    /// pas un cas d'exécution mais une erreur de câblage de route, et elle doit
    /// se voir au premier appel plutôt que de laisser passer une requête non
    /// authentifiée avec un identifiant vide.
    /// </summary>
    public static AuthenticatedPartner Require(HttpContext http)
        => http.Items[nameof(AuthenticatedPartner)] as AuthenticatedPartner
           ?? throw new InvalidOperationException(
               "Aucun partenaire authentifié : cette route doit porter AddEndpointFilter<PartnerApiKeyFilter>().");

    private static string? ReadKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue(HeaderName, out var header)
            && header.ToString() is { Length: > 0 } fromHeader)
        {
            return fromHeader.Trim();
        }

        // « Authorization: Bearer » est accepté aussi : c'est le réflexe de la
        // moitié des intégrateurs, et le refuser ne produit qu'un ticket de
        // support par partenaire.
        var authorization = request.Headers.Authorization.ToString();

        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization[7..].Trim()
            : null;
    }

    private static IResult Unauthorized(string title, string detail)
        => Results.Problem(title: title, detail: detail, statusCode: StatusCodes.Status401Unauthorized);
}
