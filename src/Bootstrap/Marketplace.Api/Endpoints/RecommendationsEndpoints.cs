using System.Security.Claims;
using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Recommendations.Application.Recommendations;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Recommendations : suggestions par produit (Similar /
/// FrequentlyBoughtTogether) ou personnalisées. Read model rafraîchi en
/// arrière-plan ; lecture publique, upsert réservé au batch (Admin).
/// </summary>
public static class RecommendationsEndpoints
{
    /// <summary>Enregistre les routes de recommandation.</summary>
    public static IEndpointRouteBuilder MapRecommendationsEndpoints(this IEndpointRouteBuilder app)
    {
        var reco = app.MapGroup("/api/recommendations").WithTags("Recommendations");

        reco.MapPost("/", UpsertAsync).WithName("UpsertRecommendation").RequireAuthorization(p => p.RequireRole("Admin"));
        reco.MapGet("/product/{productId:guid}", ByProductAsync).WithName("GetProductRecommendations").AllowAnonymous();
        reco.MapGet("/me", ForUserAsync).WithName("GetUserRecommendations").RequireAuthorization();

        return app;
    }

    /// <summary>Crée ou rafraîchit une recommandation (batch / moteur de règles).</summary>
    private static async Task<IResult> UpsertAsync(UpsertBody body, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(
            new UpsertRecommendationCommand(body.Type, body.ContextProductId, body.UserId, body.RecommendedProductIds, body.Score), ct);
        return result.Match(id => Results.Ok(new { id }));
    }

    /// <summary>Recommandations associées à un produit (type Similar par défaut).</summary>
    private static async Task<IResult> ByProductAsync(Guid productId, string? type, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetProductRecommendationsQuery(productId, type ?? "Similar"), ct)).Match(r => Results.Ok(r));

    /// <summary>Recommandations personnalisées de l'utilisateur connecté.</summary>
    private static async Task<IResult> ForUserAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId))
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new GetUserRecommendationsQuery(userId), ct);
        return result.Match(r => Results.Ok(r));
    }

    public sealed record UpsertBody(string Type, Guid? ContextProductId, Guid? UserId, IReadOnlyList<Guid> RecommendedProductIds, double Score);
}
