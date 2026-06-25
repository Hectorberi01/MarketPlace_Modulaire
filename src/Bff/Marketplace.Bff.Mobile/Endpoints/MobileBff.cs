using System.Security.Claims;

namespace Marketplace.Bff.Mobile.Endpoints;

/// <summary>Utilitaires partagés des endpoints /mobile (identité issue du jeton).</summary>
internal static class MobileBff
{
    /// <summary>Identifiant de l'utilisateur courant, extrait du JWT (jamais du client).</summary>
    public static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>Réponse 501 normalisée pour un endpoint dont le module sous-jacent manque encore.</summary>
    public static IResult NotImplemented(string capability)
        => Results.Json(new { error = "not_implemented", capability }, statusCode: StatusCodes.Status501NotImplemented);
}
