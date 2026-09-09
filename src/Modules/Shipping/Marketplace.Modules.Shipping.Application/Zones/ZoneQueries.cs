using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Geography;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Shipping.Contracts;
using Marketplace.Modules.Shipping.Domain.Zones;

namespace Marketplace.Modules.Shipping.Application.Zones;

/// <summary>
/// Options de livraison applicables à une commune de destination.
///
/// <paramref name="CommuneCode"/> peut être nul ou inconnu : l'acheteur consulte les
/// tarifs avant d'avoir choisi son adresse, et une commune non reconnue doit donner
/// un prix, pas une erreur.
/// </summary>
public sealed record GetShippingRatesQuery(string? CommuneCode) : IQuery<IReadOnlyList<ShippingRateOption>>;

/// <summary>Zones et grilles, pour l'administration.</summary>
public sealed record ListShippingZonesQuery : IQuery<IReadOnlyList<ShippingZoneSummary>>;

/// <summary>Communes non rattachées à une zone explicite.</summary>
public sealed record GetShippingCoverageGapQuery : IQuery<ShippingCoverageGapView>;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// LE TARIF DE SECOURS, ET POURQUOI IL EXISTE ENCORE.
///
/// Ces deux forfaits sont ceux qui étaient codés en dur dans le BFF client. Ils n'ont
/// pas été supprimés : ils ont été DÉPLACÉS ici, au seul endroit qui décide d'un
/// tarif, et rétrogradés au rang de dernier recours.
///
/// Ils s'appliquent tant qu'aucune zone active ne répond — donc, en pratique, tant
/// que personne n'a saisi de grille. Sans eux, la mise en service de la tarification
/// géographique casserait le tunnel de commande de toute la place à la seconde du
/// déploiement, pour la seule raison qu'une table est vide. Une grille incomplète
/// doit dégrader le prix, jamais le service : c'est la règle que porte déjà la zone
/// de repli, appliquée un cran plus bas.
///
/// LEUR PRÉSENCE DOIT SE VOIR. Les options qu'ils produisent portent un `ZoneCode`
/// nul, ce qui permet à l'administration de distinguer « tarif de secours » de
/// « zone configurée » — sans quoi une grille jamais saisie ressemblerait à une
/// grille qui marche.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
// PUBLIC, ET PAS `internal` : l'implémentation de l'API publique du module vit dans
// l'assembly Infrastructure et doit pouvoir retomber dessus quand la lecture échoue.
public static class FallbackRates
{
    public const string Currency = "XOF";

    public static readonly IReadOnlyList<ShippingRateOption> Options =
    [
        new("standard", "Standard", "Livraison sous 24h à 48h", 1500m, Currency, null, null),
        new("express", "Express", "Livraison en moins de 3h", 3000m, Currency, null, null),
    ];
}

internal sealed class GetShippingRatesQueryHandler
    : IQueryHandler<GetShippingRatesQuery, IReadOnlyList<ShippingRateOption>>
{
    private readonly IShippingZoneRepository _zones;

    public GetShippingRatesQueryHandler(IShippingZoneRepository zones) => _zones = zones;

    public async Task<Result<IReadOnlyList<ShippingRateOption>>> Handle(
        GetShippingRatesQuery query, CancellationToken cancellationToken)
    {
        var zones = await _zones.ListAsync(cancellationToken);

        // La commune est normalisée AVANT la résolution. Le résolveur compare des
        // codes ; lui passer « Grand-Popo » quand la zone contient « grand-popo »
        // donnerait le tarif national à une commune pourtant rattachée, et l'écart
        // ne se verrait que sur la facture.
        var communeCode = BeninGeography.ResolveCommuneCode(query.CommuneCode);

        var zone = ShippingZoneResolver.Resolve(zones, communeCode);

        // Une zone sans aucun tarif est aussi inutilisable qu'une zone absente : on
        // retombe sur le secours plutôt que de renvoyer une liste vide, qui se
        // traduirait à l'écran par « aucune livraison possible ».
        if (zone is null || zone.Rates.Count == 0)
        {
            return Result.Success(FallbackRates.Options);
        }

        return Result.Success<IReadOnlyList<ShippingRateOption>>(
            [.. zone.Rates
                .OrderBy(r => r.Amount)
                .Select(r => new ShippingRateOption(
                    r.ServiceCode, r.Label, r.Eta, r.Amount, r.Currency, zone.Code, zone.Name))]);
    }
}

internal sealed class ListShippingZonesQueryHandler
    : IQueryHandler<ListShippingZonesQuery, IReadOnlyList<ShippingZoneSummary>>
{
    private readonly IShippingZoneRepository _zones;

    public ListShippingZonesQueryHandler(IShippingZoneRepository zones) => _zones = zones;

    public async Task<Result<IReadOnlyList<ShippingZoneSummary>>> Handle(
        ListShippingZonesQuery query, CancellationToken cancellationToken)
    {
        var zones = await _zones.ListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<ShippingZoneSummary>>([.. zones.Select(ZoneMapper.ToSummary)]);
    }
}

internal sealed class GetShippingCoverageGapQueryHandler
    : IQueryHandler<GetShippingCoverageGapQuery, ShippingCoverageGapView>
{
    private readonly IShippingZoneRepository _zones;

    public GetShippingCoverageGapQueryHandler(IShippingZoneRepository zones) => _zones = zones;

    public async Task<Result<ShippingCoverageGapView>> Handle(
        GetShippingCoverageGapQuery query, CancellationToken cancellationToken)
    {
        var zones = await _zones.ListAsync(cancellationToken);

        var uncovered = ShippingZoneResolver.UncoveredCommunes(
            zones, BeninGeography.Communes.Select(c => c.Code));

        return Result.Success(new ShippingCoverageGapView(
            uncovered,
            zones.Any(z => z.IsFallback && z.IsActive)));
    }
}

internal static class ZoneMapper
{
    public static ShippingZoneSummary ToSummary(ShippingZone zone) => new(
        zone.Id.Value,
        zone.Code,
        zone.Name,
        zone.IsFallback,
        zone.IsActive,
        zone.CommuneCodes,
        [.. zone.Rates.Select(r => new ShippingRateOption(
            r.ServiceCode, r.Label, r.Eta, r.Amount, r.Currency, zone.Code, zone.Name))],
        zone.CreatedAtUtc);
}
