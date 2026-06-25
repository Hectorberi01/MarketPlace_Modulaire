using System.Security.Claims;
using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Loyalty.Application.Accounts;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Loyalty : compte de fidélité de l'utilisateur
/// connecté (solde, palier, historique). Les points sont crédités automatiquement
/// à la confirmation des commandes.
/// </summary>
public static class LoyaltyEndpoints
{
    /// <summary>Enregistre les routes de fidélité.</summary>
    public static IEndpointRouteBuilder MapLoyaltyEndpoints(this IEndpointRouteBuilder app)
    {
        var loyalty = app.MapGroup("/api/loyalty").WithTags("Loyalty").RequireAuthorization();

        loyalty.MapGet("/", GetAsync).WithName("GetMyLoyalty");
        loyalty.MapPost("/redeem", RedeemAsync).WithName("RedeemPoints");

        return app;
    }

    /// <summary>Renvoie le compte de fidélité de l'utilisateur connecté.</summary>
    private static async Task<IResult> GetAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new GetMyLoyaltyQuery(userId), ct);
        return result.Match(account => Results.Ok(account));
    }

    /// <summary>Dépense des points (cashback).</summary>
    private static async Task<IResult> RedeemAsync(RedeemBody body, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new RedeemPointsCommand(userId, body.Amount), ct);
        return result.Match(() => Results.NoContent());
    }

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    public sealed record RedeemBody(int Amount);
}
