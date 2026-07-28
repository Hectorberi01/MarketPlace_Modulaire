namespace Marketplace.Modules.Offers.Contracts;

/// <summary>
/// API in-process publique du module Offers. Permet à Cart/Ordering de lire une
/// offre (prix de base, vendeur, fulfillment) au moment de l'ajout au panier.
/// </summary>
public interface IOfferModuleApi
{
    Task<OfferSummary?> GetOfferAsync(Guid offerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OfferSummary>> GetOffersByProductAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Offres portant ce SKU. Sert à remonter d'une référence d'inventaire au VENDEUR :
    /// l'inventaire raisonne en SKU et ne connaît pas la boutique (ex. alerte de rupture).
    /// </summary>
    Task<IReadOnlyList<OfferSummary>> GetOffersBySkuAsync(string sku, CancellationToken cancellationToken = default);
}
