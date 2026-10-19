using MediatR;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Shipping.Application.Carriers;
using Marketplace.Modules.Shipping.Application.Zones;

namespace Marketplace.Host.Admin.Endpoints;

/// <summary>
/// BFF Admin — catalogue des transporteurs. L'admin gère la liste que les vendeurs
/// utilisent à l'expédition (nom, code, modèle d'URL de suivi, actif/inactif).
/// </summary>
public static class AdminShippingEndpoints
{
    public static IEndpointRouteBuilder MapAdminShippingEndpoints(this IEndpointRouteBuilder app)
    {
        var carriers = app.MapAdminGroup("/api/admin/carriers", "Admin · Transporteurs");

        carriers.MapGet("/", ListAsync);
        carriers.MapGet("/{id:guid}", GetAsync);
        carriers.MapPost("/", CreateAsync);
        carriers.MapPut("/{id:guid}", UpdateAsync);
        carriers.MapPost("/{id:guid}/activate", ActivateAsync);
        carriers.MapPost("/{id:guid}/deactivate", DeactivateAsync);

        // ─────────────────────────────────────────────────────────────────────────
        // LA GRILLE TARIFAIRE PAR ZONE.
        //
        // Tant que ces routes n'ont pas servi, la place facture les forfaits de
        // secours et Cotonou coûte le prix de Karimama. C'est ici, et nulle part
        // ailleurs, que la tarification géographique s'active — sans livraison de
        // code, ce qui était l'objet même de la mettre en base.
        //
        // `/coverage` existe pour la question qu'un administrateur ne pense jamais à
        // poser : quelles communes ai-je oublié de rattacher ? Elles marchent — le
        // repli les prend — et c'est précisément pourquoi personne ne les remarque.
        // ─────────────────────────────────────────────────────────────────────────
        var zones = app.MapAdminGroup("/api/admin/shipping-zones", "Admin · Zones de livraison");

        zones.MapGet("/", ListZonesAsync);
        zones.MapGet("/coverage", CoverageAsync);
        zones.MapGet("/rates", RatesForCommuneAsync);
        zones.MapPost("/", CreateZoneAsync);
        zones.MapPut("/{id:guid}", UpdateZoneAsync);
        zones.MapPut("/{id:guid}/rates", SetRateAsync);
        zones.MapDelete("/{id:guid}/rates/{serviceCode}", RemoveRateAsync);
        zones.MapPost("/{id:guid}/activate", ActivateZoneAsync);
        zones.MapPost("/{id:guid}/deactivate", DeactivateZoneAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListCarriersQuery(), ct)).Match(c => Results.Ok(c));

    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetCarrierQuery(id), ct)).Match(c => Results.Ok(c));

    private static async Task<IResult> CreateAsync(CarrierCreateRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateCarrierCommand(request.Name, request.Code, request.TrackingUrlTemplate, request.LogoUrl), ct))
            .Match(id => Results.Ok(new { carrierId = id }));

    private static async Task<IResult> UpdateAsync(Guid id, CarrierUpdateRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UpdateCarrierCommand(id, request.Name, request.TrackingUrlTemplate, request.LogoUrl), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> ActivateAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new SetCarrierActiveCommand(id, true), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> DeactivateAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new SetCarrierActiveCommand(id, false), ct)).Match(() => Results.NoContent());

    /// <summary>Le code est immuable après création (référencé par les expéditions).</summary>
    public sealed record CarrierCreateRequest(string Name, string Code, string? TrackingUrlTemplate, string? LogoUrl);
    public sealed record CarrierUpdateRequest(string Name, string? TrackingUrlTemplate, string? LogoUrl);

    // ── Zones de livraison ──────────────────────────────────────────────────────

    private static async Task<IResult> ListZonesAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListShippingZonesQuery(), ct)).Match(z => Results.Ok(z));

    private static async Task<IResult> CoverageAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new GetShippingCoverageGapQuery(), ct)).Match(c => Results.Ok(c));

    /// <summary>
    /// Ce que l'ACHETEUR verrait pour cette commune. Le même appel que le tunnel de
    /// commande, exposé à l'administration : c'est la seule façon de vérifier une
    /// grille sans passer une vraie commande.
    /// </summary>
    private static async Task<IResult> RatesForCommuneAsync(string? commune, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetShippingRatesQuery(commune), ct)).Match(r => Results.Ok(r));

    private static async Task<IResult> CreateZoneAsync(ZoneCreateRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateShippingZoneCommand(request.Code, request.Name, request.IsFallback), ct))
            .Match(id => Results.Ok(new { zoneId = id }));

    private static async Task<IResult> UpdateZoneAsync(Guid id, ZoneUpdateRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UpdateShippingZoneCommand(id, request.Name, request.CommuneCodes), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> SetRateAsync(Guid id, ZoneRateRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new SetShippingRateCommand(
                id, request.ServiceCode, request.Label, request.Eta, request.Amount, request.Currency), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> RemoveRateAsync(Guid id, string serviceCode, ISender sender, CancellationToken ct)
        => (await sender.Send(new RemoveShippingRateCommand(id, serviceCode), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ActivateZoneAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new SetShippingZoneActiveCommand(id, true), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> DeactivateZoneAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new SetShippingZoneActiveCommand(id, false), ct)).Match(() => Results.NoContent());

    /// <summary>`isFallback` ne peut pas être modifié après coup : une zone de repli
    /// rétrogradée laisserait les communes non rattachées sans aucun tarif.</summary>
    public sealed record ZoneCreateRequest(string Code, string Name, bool IsFallback = false);

    /// <summary>`communeCodes` REMPLACE la liste : ce qui n'y est pas est détaché.</summary>
    public sealed record ZoneUpdateRequest(string Name, IReadOnlyList<string>? CommuneCodes);

    public sealed record ZoneRateRequest(
        string ServiceCode, string Label, string? Eta, decimal Amount, string? Currency = "XOF");
}
