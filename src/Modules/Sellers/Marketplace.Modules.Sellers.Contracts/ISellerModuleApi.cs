namespace Marketplace.Modules.Sellers.Contracts;

/// <summary>
/// API in-process publique du module Sellers. Permet par exemple à Catalog de
/// vérifier qu'un vendeur est actif avant d'accepter la création d'un produit.
/// </summary>
public interface ISellerModuleApi
{
    Task<SellerSummary?> GetSellerAsync(Guid sellerId, CancellationToken cancellationToken = default);

    Task<SellerSummary?> GetSellerByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> IsActiveSellerAsync(Guid sellerId, CancellationToken cancellationToken = default);
}
