using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Reviews.Contracts;
using Marketplace.Modules.Reviews.Contracts.IntegrationEvents;
using Marketplace.Modules.Sellers.Application.Abstractions;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Application.Sellers.EventHandlers;

/// <summary>
/// À la confirmation d'une commande, met à jour le compteur de ventes de chaque
/// vendeur concerné. On RECALCULE le total depuis Ordering (somme des quantités
/// vendues sur les commandes encaissées) plutôt que d'incrémenter : le dispatch
/// d'événements est « au moins une fois », donc un incrément double-compterait si
/// l'événement était rejoué. Poser le total exact est idempotent.
/// </summary>
public sealed class RefreshSellerSalesOnOrderConfirmedHandler : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    private readonly ISellerRepository _sellers;
    private readonly IOrderingModuleApi _ordering;
    private readonly ISellerUnitOfWork _unitOfWork;

    public RefreshSellerSalesOnOrderConfirmedHandler(
        ISellerRepository sellers, IOrderingModuleApi ordering, ISellerUnitOfWork unitOfWork)
    {
        _sellers = sellers;
        _ordering = ordering;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(OrderConfirmedIntegrationEvent e, CancellationToken cancellationToken = default)
    {
        var changed = false;
        foreach (var share in e.SellerShares)
        {
            var seller = await _sellers.GetByIdAsync(new SellerId(share.SellerId), cancellationToken);
            if (seller is null)
            {
                continue;
            }

            var total = await _ordering.GetSellerSalesCountAsync(share.SellerId, cancellationToken);
            seller.SetSalesCount(total);
            changed = true;
        }

        if (changed)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>
/// Après publication d'un avis, rafraîchit la note moyenne du vendeur dont c'est
/// le produit. Recalcul depuis Reviews (moyenne sur tous ses produits) — idempotent.
/// </summary>
public sealed class RefreshSellerRatingOnReviewPublishedHandler : IIntegrationEventHandler<ReviewPublishedIntegrationEvent>
{
    private readonly SellerRatingRefresher _refresher;

    public RefreshSellerRatingOnReviewPublishedHandler(SellerRatingRefresher refresher) => _refresher = refresher;

    public Task HandleAsync(ReviewPublishedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _refresher.RefreshForProductAsync(e.ProductId, cancellationToken);
}

/// <summary>Après rejet d'un avis (retiré du calcul), rafraîchit la note du vendeur.</summary>
public sealed class RefreshSellerRatingOnReviewRejectedHandler : IIntegrationEventHandler<ReviewRejectedIntegrationEvent>
{
    private readonly SellerRatingRefresher _refresher;

    public RefreshSellerRatingOnReviewRejectedHandler(SellerRatingRefresher refresher) => _refresher = refresher;

    public Task HandleAsync(ReviewRejectedIntegrationEvent e, CancellationToken cancellationToken = default)
        => _refresher.RefreshForProductAsync(e.ProductId, cancellationToken);
}

/// <summary>
/// Logique partagée entre publication et rejet d'avis : résoudre le vendeur du
/// produit (via Catalog, car l'événement d'avis ne porte que le produit), relire
/// la note agrégée du vendeur (via Reviews) et la persister sur l'entité vendeur.
/// </summary>
public sealed class SellerRatingRefresher
{
    private readonly ISellerRepository _sellers;
    private readonly ICatalogModuleApi _catalog;
    private readonly IReviewsModuleApi _reviews;
    private readonly ISellerUnitOfWork _unitOfWork;

    public SellerRatingRefresher(
        ISellerRepository sellers, ICatalogModuleApi catalog, IReviewsModuleApi reviews, ISellerUnitOfWork unitOfWork)
    {
        _sellers = sellers;
        _catalog = catalog;
        _reviews = reviews;
        _unitOfWork = unitOfWork;
    }

    public async Task RefreshForProductAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await _catalog.GetProductAsync(productId, cancellationToken);
        if (product is null)
        {
            return;
        }

        var seller = await _sellers.GetByIdAsync(new SellerId(product.SellerId), cancellationToken);
        if (seller is null)
        {
            return;
        }

        var rating = await _reviews.GetSellerRatingAsync(product.SellerId, cancellationToken);
        var result = seller.UpdateRating((decimal)rating.Average);
        if (result.IsSuccess)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
