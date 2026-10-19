using MediatR;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Pricing.Application.Pricing.Queries;
using Marketplace.Modules.Pricing.Application.Promotions.Commands;
using Marketplace.Modules.Pricing.Application.Promotions.Commands.CreatePromotion;
using Marketplace.Modules.Pricing.Application.Promotions.Queries;

namespace Marketplace.Host.Endpoints;

/// <summary>
/// Endpoints HTTP du module Pricing &amp; Promotions : gestion des promotions et
/// calcul du prix effectif (prix de base + réductions tracées par financeur).
/// </summary>
public static class PricingEndpoints
{
    /// <summary>Enregistre les routes des promotions et du calcul de prix.</summary>
    public static IEndpointRouteBuilder MapPricingEndpoints(this IEndpointRouteBuilder app)
    {
        var promos = app.MapAdminGroup("/api/pricing/promotions").WithTags("Pricing · Promotions").RequireAuthorization();

        promos.MapPost("/", CreateAsync).WithName("CreatePromotion");
        promos.MapGet("/", ListAsync).WithName("ListPromotions").RequireAuthorization(p => p.RequireRole("Admin"));
        promos.MapGet("/{id:guid}", GetAsync).WithName("GetPromotion");
        promos.MapPost("/{id:guid}/activate", ActivateAsync).WithName("ActivatePromotion");
        promos.MapPost("/{id:guid}/pause", PauseAsync).WithName("PausePromotion");
        promos.MapPost("/{id:guid}/expire", ExpireAsync).WithName("ExpirePromotion");

        // Calcul du prix effectif : aperçu public (consommé surtout en in-process par Cart/Ordering).
        app.MapPost("/api/pricing/calculate", CalculateAsync)
            .WithTags("Pricing · Calcul").WithName("CalculateEffectivePrice").AllowAnonymous();

        return app;
    }

    /// <summary>Crée une promotion (brouillon) : périmètre, financeur, conditions, période.</summary>
    private static async Task<IResult> CreateAsync(CreatePromotionRequest request, ISender sender, CancellationToken ct)
    {
        var command = new CreatePromotionCommand(
            request.OwnerType, request.OwnerId, request.FundedBy, request.Type, request.Value, request.ScopeType,
            request.Targets ?? new List<Guid>(), request.MinAmount, request.MinQuantity, request.FirstOrderOnly,
            request.Code, request.StartUtc, request.EndUtc, request.UsageLimit);

        var result = await sender.Send(command, ct);
        return result.Match(id => Results.Created($"/api/pricing/promotions/{id}", new { id }));
    }

    /// <summary>Liste les promotions (Admin).</summary>
    private static async Task<IResult> ListAsync(ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListPromotionsQuery(), ct);
        return result.Match(promotions => Results.Ok(promotions));
    }

    /// <summary>Récupère une promotion par son identifiant.</summary>
    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetPromotionQuery(id), ct);
        return result.Match(summary => Results.Ok(summary));
    }

    /// <summary>Active une promotion.</summary>
    private static async Task<IResult> ActivateAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ActivatePromotionCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Met une promotion en pause.</summary>
    private static async Task<IResult> PauseAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new PausePromotionCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Expire (clôt) une promotion.</summary>
    private static async Task<IResult> ExpireAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ExpirePromotionCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Calcule le prix effectif d'une ligne (prix de base + promotions, financeurs tracés).</summary>
    private static async Task<IResult> CalculateAsync(CalculateRequest request, ISender sender, CancellationToken ct)
    {
        var query = new CalculateEffectivePriceQuery(
            request.BaseAmount, request.Currency, request.ProductId, request.CategoryId, request.SellerId,
            request.Quantity, request.Subtotal, request.Code, request.IsFirstOrder);

        var result = await sender.Send(query, ct);
        return result.Match(breakdown => Results.Ok(breakdown));
    }

    public sealed record CreatePromotionRequest(
        string OwnerType, Guid OwnerId, string FundedBy, string Type, decimal Value, string ScopeType,
        IReadOnlyList<Guid>? Targets, decimal? MinAmount, int? MinQuantity, bool FirstOrderOnly,
        string? Code, DateTime StartUtc, DateTime EndUtc, int UsageLimit);

    public sealed record CalculateRequest(
        decimal BaseAmount, string Currency, Guid ProductId, Guid CategoryId, Guid SellerId,
        int Quantity, decimal Subtotal, string? Code, bool IsFirstOrder);
}
