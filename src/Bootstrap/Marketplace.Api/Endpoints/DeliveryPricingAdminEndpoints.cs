using HBA.Deliveries.Application.Pricing.Commands;
using HBA.Deliveries.Application.Pricing.Queries;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using Marketplace.Api.Extensions;
using MediatR;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ADMINISTRATION DE LA TARIFICATION HBA DELIVERY.
///
/// Ces routes n'existaient pas : seul l'amorçage écrivait dans les tables de zones
/// et de grilles, et un tarif ne s'ajustait qu'en base. C'est particulièrement
/// gênant pour le FACTEUR ROUTIER, qui est conçu pour se calibrer à mesure que de
/// vraies courses sont livrées.
///
/// ⚠️ IL N'Y A PAS DE « MODIFIER UNE GRILLE », ET CE N'EST PAS UN OUBLI.
///
/// Chaque devis conserve l'identifiant et la VERSION de la grille qui l'a produit,
/// pour qu'un montant contesté puisse être refait six mois plus tard. Modifier une
/// grille en place rendrait tous ces devis irreproduisibles — et un devis qu'on ne
/// peut pas refaire n'est pas opposable.
///
/// Le geste correct est : publier la nouvelle grille, désactiver l'ancienne.
///
/// GROUPE ADMINISTRATEUR, ET C'EST LE MINIMUM
///
/// Ce sont les PRIX. Les cinq hôtes partagent la même clé de signature JWT : un
/// jeton acheteur est valide sur cette API, et ne doit évidemment pas suffire à
/// publier une grille tarifaire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DeliveryPricingAdminEndpoints
{
    public static IEndpointRouteBuilder MapDeliveryPricingAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var zones = app.MapAdminGroup("/api/delivery/zones")
            .WithTags("HBA Delivery · Administration tarifaire");

        zones.MapGet("/", ListZonesAsync).WithName("ListDeliveryZones");
        zones.MapPost("/", CreateZoneAsync).WithName("CreateDeliveryZone");

        // PATCH et non PUT : on ajuste un aspect précis d'une zone existante, on ne
        // la remplace pas. Un PUT inviterait à renvoyer le contour entier à chaque
        // recalibrage, et un contour retransmis est un contour qu'on peut abîmer.
        zones.MapPatch("/{id:guid}/road-factor", RecalibrateAsync).WithName("RecalibrateDeliveryZone");
        zones.MapPatch("/{id:guid}/fees", UpdateFeesAsync).WithName("UpdateDeliveryZoneFees");
        zones.MapPatch("/{id:guid}/active", SetActiveAsync).WithName("SetDeliveryZoneActive");

        var rules = app.MapAdminGroup("/api/delivery/pricing-rules")
            .WithTags("HBA Delivery · Administration tarifaire");

        rules.MapGet("/", ListRulesAsync).WithName("ListDeliveryPricingRules");
        rules.MapPost("/", CreateRuleAsync).WithName("CreateDeliveryPricingRule");
        rules.MapDelete("/{id:guid}", DeactivateRuleAsync).WithName("DeactivateDeliveryPricingRule");

        return app;
    }

    // ── Zones ───────────────────────────────────────────────────────────────

    private static async Task<IResult> ListZonesAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListDeliveryZonesQuery(), ct)).Match(z => Results.Ok(z));

    private static async Task<IResult> CreateZoneAsync(
        CreateZoneRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(
                new CreateDeliveryZoneCommand(
                    request.Name, request.City,
                    request.Boundary.Select(p => new GeoPointInput(p.Latitude, p.Longitude)).ToList(),
                    request.BaseFee, request.ExtraFee, request.RoadFactor, request.Priority), ct))
            .Match(id => Results.Created($"/api/delivery/zones/{id}", new { zoneId = id }));

    /// <summary>
    /// Le calibrage. C'est l'opération destinée à être RÉPÉTÉE : on compare la
    /// distance réelle des courses livrées à l'orthodromie, et on ajuste.
    ///
    /// Le domaine refuse tout facteur hors de [1, 5] : en dessous de 1, la route
    /// serait plus courte que la ligne droite — la faute de frappe la plus
    /// probable, une virgule mal placée.
    /// </summary>
    private static async Task<IResult> RecalibrateAsync(
        Guid id, RoadFactorRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new RecalibrateZoneCommand(id, request.RoadFactor), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> UpdateFeesAsync(
        Guid id, FeesRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UpdateZoneFeesCommand(id, request.BaseFee, request.ExtraFee), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> SetActiveAsync(
        Guid id, ActiveRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new SetZoneActiveCommand(id, request.Active), ct))
            .Match(() => Results.NoContent());

    // ── Grilles ─────────────────────────────────────────────────────────────

    private static async Task<IResult> ListRulesAsync(
        DateTime? atUtc, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListPricingRulesQuery(atUtc), ct)).Match(r => Results.Ok(r));

    private static async Task<IResult> CreateRuleAsync(
        CreateRuleRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(
                new CreatePricingRuleCommand(
                    request.Name, request.BasePrice, request.PricePerKm, request.MinimumPrice,
                    request.MaximumDistanceKm, request.DeliveryType, request.VehicleType,
                    request.UrgencySurcharge, request.ValidFromUtc, request.ValidUntilUtc, request.Version,
                    request.WeightTiers?.Select(t => new WeightTierInput(t.FromKg, t.ToKg, t.Surcharge)).ToList()),
                ct))
            .Match(id => Results.Created($"/api/delivery/pricing-rules/{id}", new { pricingRuleId = id }));

    /// <summary>
    /// Retire une grille du calcul.
    ///
    /// ⚠️ DELETE, mais RIEN N'EST SUPPRIMÉ. La grille est désactivée et reste en
    /// base : les devis déjà émis pointent vers elle, et les effacer rendrait
    /// impossible de refaire leur calcul. Le verbe HTTP décrit l'intention de
    /// l'appelant — « je ne veux plus de cette grille » — pas le traitement.
    /// </summary>
    private static async Task<IResult> DeactivateRuleAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeactivatePricingRuleCommand(id), ct))
            .Match(() => Results.NoContent());

    // ── Corps de requête ────────────────────────────────────────────────────

    public sealed record PointRequest(double Latitude, double Longitude);

    /// <summary>
    /// <c>Priority</c> doit être UNIQUE parmi les zones actives. À priorité égale,
    /// deux zones qui se recouvrent seraient départagées par un GUID — et deux
    /// appels factureraient différemment la même course.
    /// </summary>
    public sealed record CreateZoneRequest(
        string? Name,
        string? City,
        IReadOnlyList<PointRequest> Boundary,
        decimal BaseFee = 0m,
        decimal ExtraFee = 0m,
        double RoadFactor = 1.4,
        int Priority = 0);

    public sealed record RoadFactorRequest(double RoadFactor);

    public sealed record FeesRequest(decimal BaseFee, decimal ExtraFee);

    public sealed record ActiveRequest(bool Active);

    public sealed record TierRequest(decimal FromKg, decimal? ToKg, decimal Surcharge);

    public sealed record CreateRuleRequest(
        string? Name,
        decimal BasePrice,
        decimal PricePerKm,
        decimal MinimumPrice = 0m,
        double? MaximumDistanceKm = null,
        DeliveryType? DeliveryType = null,
        VehicleType? VehicleType = null,
        decimal UrgencySurcharge = 0m,
        DateTime? ValidFromUtc = null,
        DateTime? ValidUntilUtc = null,
        int Version = 1,
        IReadOnlyList<TierRequest>? WeightTiers = null);
}
