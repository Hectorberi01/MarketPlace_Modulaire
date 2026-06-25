using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Offers.Domain.Offers;
using Marketplace.Modules.Offers.Infrastructure.Persistence;

namespace Marketplace.Modules.Offers.Infrastructure.Public;

/// <summary>Implémentation in-process de l'API publique du module Offers (lecture seule).</summary>
internal sealed class OfferModuleApi : IOfferModuleApi
{
    private readonly OffersDbContext _dbContext;

    public OfferModuleApi(OffersDbContext dbContext)
        => _dbContext = dbContext;

    public async Task<OfferSummary?> GetOfferAsync(Guid offerId, CancellationToken cancellationToken = default)
    {
        var id = new OfferId(offerId);
        var offer = await _dbContext.Offers.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        return offer is null ? null : Map(offer);
    }

    public async Task<IReadOnlyList<OfferSummary>> GetOffersByProductAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var offers = await _dbContext.Offers
            .AsNoTracking()
            .Where(o => o.ProductId == productId && o.Status == OfferStatus.Active)
            .ToListAsync(cancellationToken);

        return offers.Select(Map).ToList();
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
        offer.Status.ToString());
}
