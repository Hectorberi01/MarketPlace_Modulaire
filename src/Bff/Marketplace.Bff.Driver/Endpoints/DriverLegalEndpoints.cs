using MediatR;
using Marketplace.Modules.Marketing.Application.Content;

namespace Marketplace.Bff.Driver.Endpoints;

/// <summary>
/// BFF Coursier — les textes contractuels, avec leur version.
///
/// ═════════════════════════════════════════════════════════════════════════════════════
/// LE COURSIER N'ACCEPTAIT RIEN DU TOUT.
///
/// Les deux autres applications ont un écran de consentement et un texte ; celle du
/// coursier n'avait ni l'un ni l'autre — aucun écran juridique, aucune route, rien. Or
/// c'est la surface dont la relation contractuelle est la plus engageante : le coursier
/// prend en charge les biens d'un tiers, roule, et transmet sa position pendant qu'il
/// travaille.
/// ═════════════════════════════════════════════════════════════════════════════════════
///
/// PUBLIQUE, comme les deux autres : on doit pouvoir lire les conditions d'une
/// plateforme de livraison avant d'y postuler, pas seulement une fois recruté.
/// </summary>
public static class DriverLegalEndpoints
{
    /// <summary>
    /// Les documents servis, et leur emplacement dans le CMS.
    ///
    /// LISTE FERMÉE, et DISTINCTE de celles de l'acheteur et du vendeur. Le coursier
    /// n'a ni les obligations d'un vendeur ni les droits d'un acheteur : il fournit une
    /// prestation de transport. Lui servir l'un des deux autres textes lui ferait
    /// accepter un contrat écrit pour quelqu'un d'autre — et, pour la confidentialité,
    /// un texte qui ne parlerait pas de la seule donnée qui le concerne vraiment, sa
    /// position.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Emplacements =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["terms"] = "legal_terms_driver",
            ["privacy"] = "legal_privacy_driver",
        };

    public static IEndpointRouteBuilder MapDriverLegalEndpoints(this IEndpointRouteBuilder app)
    {
        var legal = app.MapGroup("/driver/legal").WithTags("Coursier · Légal").AllowAnonymous();

        legal.MapGet("/{document}", GetAsync).WithName("DriverLegalDocument");

        return app;
    }

    /// <summary>
    /// 404 QUAND LE CMS N'A RIEN, et l'appelant retombe sur sa copie embarquée en faisant
    /// accepter LA VERSION DE CETTE COPIE. Le coursier accepte toujours le texte qu'il a
    /// sous les yeux — jamais un autre.
    /// </summary>
    private static async Task<IResult> GetAsync(
        string document, ISender sender, CancellationToken ct)
    {
        if (!Emplacements.TryGetValue(document ?? string.Empty, out var emplacement))
        {
            return Results.NotFound(new { error = "unknown_document" });
        }

        var resultat = await sender.Send(new GetLegalDocumentQuery(emplacement), ct);
        if (resultat.IsFailure)
        {
            return Results.NotFound(new { error = "document_unavailable" });
        }

        var doc = resultat.Value;
        return Results.Ok(new
        {
            document,
            version = doc.Version,
            lastUpdated = doc.LastUpdated,
            company = doc.Company,
            sections = doc.Sections.Select(s => new { title = s.Title, body = s.Body }),
        });
    }
}
