namespace Marketplace.Modules.Offers.Domain.Offers;

public interface IOfferRepository
{
    Task AddAsync(Offer offer, CancellationToken cancellationToken = default);

    void Remove(Offer offer);

    Task<Offer?> GetByIdAsync(OfferId id, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid sellerId, string sku, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Offer>> ListByProductAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Offer>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Offres dont la remise a expiré (DiscountEndsOnUtc dépassé et remise active).
    /// Résultat TRACKÉ : destiné à retirer la remise. Limité à <paramref name="take"/>.
    /// </summary>
    Task<IReadOnlyList<Offer>> ListExpiredDiscountsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default);
}
