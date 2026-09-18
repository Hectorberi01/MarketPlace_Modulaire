using MediatR;
using Marketplace.Modules.Marketing.Application.Content;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — les textes contractuels, avec leur version.
///
/// ═════════════════════════════════════════════════════════════════════════════════════
/// LE VENDEUR SIGNAIT SANS QUE RIEN NE LUI SOIT MONTRÉ.
///
/// La console web affichait « Conditions acceptées : version X » sur la page Compte, et
/// n'avait NI page de conditions, NI écran d'acceptation. La version affichée venait donc
/// d'une acceptation faite ailleurs — sur le téléphone — ou de nulle part.
///
/// Cette route sert le texte ET la version que l'acceptation consigne, pour que les deux
/// surfaces vendeur montrent la même chose et fassent accepter la même chose.
/// ═════════════════════════════════════════════════════════════════════════════════════
///
/// PUBLIQUE, comme côté acheteur : on doit pouvoir lire les conditions d'une place de
/// marché avant d'y ouvrir une boutique, pas seulement après.
/// </summary>
public static class SellerLegalEndpoints
{
    /// <summary>
    /// Les documents servis, et leur emplacement dans le CMS.
    ///
    /// LISTE FERMÉE, et DISTINCTE de celle de l'acheteur : le fond juridique est commun,
    /// la rédaction ne l'est pas — ici ce sont les obligations du vendeur qui sont
    /// énoncées, pas les droits de l'acheteur. Servir le même bloc aux deux surfaces
    /// ferait accepter au vendeur un texte écrit pour quelqu'un d'autre.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Emplacements =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["terms"] = "legal_terms_seller",
            ["privacy"] = "legal_privacy_seller",
        };

    public static IEndpointRouteBuilder MapSellerLegalEndpoints(this IEndpointRouteBuilder app)
    {
        var legal = app.MapGroup("/seller/legal").WithTags("Vendeur · Légal").AllowAnonymous();

        legal.MapGet("/{document}", GetAsync).WithName("SellerLegalDocument");

        return app;
    }

    /// <summary>
    /// 404 QUAND LE CMS N'A RIEN, et l'appelant retombe sur sa copie embarquée en faisant
    /// accepter LA VERSION DE CETTE COPIE. Le vendeur accepte toujours le texte qu'il a
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
