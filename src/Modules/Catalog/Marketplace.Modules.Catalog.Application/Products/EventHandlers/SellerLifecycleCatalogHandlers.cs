using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Catalog.Domain.Products;
using Marketplace.Modules.Sellers.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Catalog.Application.Products.EventHandlers;

/// <summary>
/// Fermeture d'un compte vendeur (suppression partielle) : on RETIRE ses produits de
/// la vente. On les dépublie (Active -> Draft) plutôt que de les archiver : le retrait
/// doit être RÉVERSIBLE, puisque le vendeur peut demander une réactivation et
/// republier ses fiches d'un geste.
/// </summary>
public sealed class SellerClosedProductInvalidationHandler : IIntegrationEventHandler<SellerClosedIntegrationEvent>
{
    private readonly IProductRepository _products;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILogger<SellerClosedProductInvalidationHandler> _logger;

    public SellerClosedProductInvalidationHandler(
        IProductRepository products,
        ICatalogUnitOfWork unitOfWork,
        ILogger<SellerClosedProductInvalidationHandler> logger)
    {
        _products = products;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(SellerClosedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var products = await _products.ListBySellerForUpdateAsync(e.SellerId, cancellationToken);

        var unpublished = 0;
        foreach (var product in products)
        {
            if (product.Status == ProductStatus.Active)
            {
                product.Unpublish();
                unpublished++;
            }
        }

        if (unpublished > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Vendeur {SellerId} fermé : {Count} produit(s) dépublié(s).", e.SellerId, unpublished);
    }
}

/// <summary>
/// Suppression DÉFINITIVE d'un vendeur (admin) : on ARCHIVE tous ses produits
/// (transition terminale) pour les retirer irrévocablement de la vente. On ne les
/// supprime pas physiquement : ils restent référencés par l'historique de commandes.
/// </summary>
public sealed class SellerDeletedProductPurgeHandler : IIntegrationEventHandler<SellerDeletedIntegrationEvent>
{
    private readonly IProductRepository _products;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILogger<SellerDeletedProductPurgeHandler> _logger;

    public SellerDeletedProductPurgeHandler(
        IProductRepository products,
        ICatalogUnitOfWork unitOfWork,
        ILogger<SellerDeletedProductPurgeHandler> logger)
    {
        _products = products;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(SellerDeletedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var products = await _products.ListBySellerForUpdateAsync(e.SellerId, cancellationToken);

        var archived = 0;
        foreach (var product in products)
        {
            if (product.Status != ProductStatus.Archived)
            {
                product.Archive();
                archived++;
            }
        }

        if (archived > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Vendeur {SellerId} supprimé : {Count} produit(s) archivé(s).", e.SellerId, archived);
    }
}
