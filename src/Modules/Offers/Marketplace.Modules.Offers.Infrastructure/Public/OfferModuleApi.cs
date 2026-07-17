using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Offers.Application;
using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Offers.Domain.Offers;
using Marketplace.Modules.Offers.Infrastructure.Persistence;

namespace Marketplace.Modules.Offers.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Offers (lecture seule).
///
/// Mise en cache TRÈS COURTE (60 s) : ce module porte les prix. Voir l'avertissement
/// dans OffersCacheKeys avant d'allonger quoi que ce soit.
/// </summary>
internal sealed class OfferModuleApi : IOfferModuleApi
{
    private readonly OffersDbContext _dbContext;
    private readonly ICacheService _cache;

    public OfferModuleApi(OffersDbContext dbContext, ICacheService cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public Task<OfferSummary?> GetOfferAsync(Guid offerId, CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            OffersCacheKeys.Offer(offerId),
            async ct =>
            {
                var id = new OfferId(offerId);
                var offer = await _dbContext.Offers.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
                return offer is null ? null : Map(offer);
            },
            OffersCacheKeys.OfferTtl,
            OffersCacheKeys.MissTtl,
            cancellationToken);

    public async Task<IReadOnlyList<OfferSummary>> GetOffersByProductAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        // Lue à chaque affichage de fiche produit. C'est la clé chaude du module.
        var offers = await _cache.GetOrCreateAsync(
            OffersCacheKeys.OffersByProduct(productId),
            async ct =>
            {
                var rows = await _dbContext.Offers
                    .AsNoTracking()
                    .Where(o => o.ProductId == productId && o.Status == OfferStatus.Active)
                    .ToListAsync(ct);

                return rows.Select(Map).ToList();
            },
            OffersCacheKeys.OfferTtl,
            cancellationToken: cancellationToken);

        // Un produit sans offre active est un cas NORMAL (rupture, offres retirées),
        // pas une absence : on renvoie une liste vide, comme avant.
        return offers ?? [];
    }

    private static OfferSummary Map(Offer offer) => new(
        offer.Id.Value,
        offer.ProductId,
        offer.SellerId,
        offer.VariantSku.Value,
        offer.BasePrice.Amount,
        offer.BasePrice.Currency,
        offer.Condition.ToString(),
        offer.FulfillmentType.ToString(),
        offer.ShipFromLocationId,
        offer.HandlingTime,
        offer.Status.ToString(),
        offer.SellerPrice,
        offer.CommissionAmount,
        offer.ProviderFeeAmount,
        offer.BasePrice.Amount);
}
