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
/// ═════════════════════════════════════════════════════════════════════════════════
/// SUSPENSION D'UN VENDEUR : SES PRODUITS QUITTENT LA VITRINE.
///
/// Ce consommateur n'existait pas, et il n'y avait rien à consommer : `Suspend()`
/// n'émettait aucun événement. Une suspension était donc invisible côté acheteur —
/// vitrine servie, offres toujours dans les fiches produit, commandes toujours
/// possibles. La sanction ne coûtait rien à celui qui la subissait.
///
/// ON DÉPUBLIE, ON N'ARCHIVE PAS. Même choix que pour la fermeture de compte, et
/// pour la même raison : une suspension est par nature révocable. `Draft` se
/// republie d'un geste après la levée ; `Archived` est une transition que le
/// vendeur ne peut pas défaire lui-même (§7.5).
///
/// LA LEVÉE NE REPUBLIE PAS. Il n'y a délibérément pas de consommateur symétrique :
/// republier en masse rendrait visibles les fiches que le vendeur avait lui-même
/// retirées AVANT la sanction. La reprise de vitrine est son geste, pas le nôtre.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SellerSuspendedProductUnpublishHandler : IIntegrationEventHandler<SellerSuspendedIntegrationEvent>
{
    private readonly IProductRepository _products;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly ILogger<SellerSuspendedProductUnpublishHandler> _logger;

    public SellerSuspendedProductUnpublishHandler(
        IProductRepository products,
        ICatalogUnitOfWork unitOfWork,
        ILogger<SellerSuspendedProductUnpublishHandler> logger)
    {
        _products = products;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(SellerSuspendedIntegrationEvent e, CancellationToken cancellationToken = default)
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

        // Le rejeu est sans effet : au second passage plus aucun produit n'est
        // `Active`, la boucle ne dépublie rien et rien n'est écrit.
        _logger.LogInformation(
            "Vendeur {SellerId} suspendu : {Count} produit(s) dépublié(s).", e.SellerId, unpublished);
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
