using System.Security.Claims;
using Marketplace.Modules.Sellers.Contracts;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// Utilitaires partagés des endpoints /seller. La boutique (SellerId) n'est jamais
/// fournie par le client : elle est résolue à partir de l'utilisateur du jeton,
/// ce qui garantit qu'un vendeur n'agit que sur sa propre boutique.
/// </summary>
internal static class SellerBff
{
    /// <summary>Identifiant de l'utilisateur courant, extrait du JWT.</summary>
    public static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>Boutique rattachée à l'utilisateur du jeton (null s'il n'est pas vendeur).</summary>
    public static async Task<SellerSummary?> ResolveSellerAsync(
        ClaimsPrincipal principal, ISellerModuleApi sellers, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return null;
        }

        return await sellers.GetSellerByUserIdAsync(userId, ct);
    }

    /// <summary>403 normalisé : l'utilisateur authentifié n'a pas (encore) de boutique.</summary>
    public static IResult NotASeller()
        => Results.Json(new { error = "not_a_seller", hint = "Onboardez la boutique via POST /seller/shop." },
            statusCode: StatusCodes.Status403Forbidden);

    /// <summary>Réponse 501 normalisée pour une capacité non encore exposée par un module.</summary>
    public static IResult NotImplemented(string capability)
        => Results.Json(new { error = "not_implemented", capability }, statusCode: StatusCodes.Status501NotImplemented);
}
