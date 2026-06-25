namespace Marketplace.Modules.Catalog.Contracts;

/// <summary>
/// API in-process publique du module Catalog. C'est le SEUL moyen pour un autre
/// module (Offers, Cart…) de lire du catalogue de façon synchrone — jamais en
/// touchant la base ou les entités internes. Le jour de l'extraction, cette
/// interface devient un client HTTP/gRPC sans changer les appelants.
/// </summary>
public interface ICatalogModuleApi
{
    Task<ProductSummary?> GetProductAsync(Guid productId, CancellationToken cancellationToken = default);

    Task<BrandSummary?> GetBrandAsync(Guid brandId, CancellationToken cancellationToken = default);

    Task<CategorySummary?> GetCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
