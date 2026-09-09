using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Reviews.Domain.Reviews;

namespace Marketplace.Modules.Reviews.Infrastructure.Persistence;

internal sealed class ReviewRepository : IReviewRepository
{
    private readonly ReviewsDbContext _dbContext;

    public ReviewRepository(ReviewsDbContext dbContext) => _dbContext = dbContext;

    /// <summary>
    /// Filtre de VISIBILITÉ PUBLIQUE : ce qu'un visiteur voit, et ce qui compte dans les
    /// notes agrégées.
    ///
    /// ════════════════════════════════════════════════════════════════════════════
    /// UN AVIS SIGNALÉ RESTE VISIBLE. SEUL UN REJET DE MODÉRATEUR LE RETIRE.
    ///
    /// Les trois lectures ci-dessous filtraient sur `Published` SEUL. `Flagged` étant
    /// posé par `Review.Flag()`, et `POST /api/reviews/{id}/flag` n'exigeant aucun rôle,
    /// n'importe quel compte authentifié — créé la minute d'avant — dépubliait l'avis de
    /// son choix. Avis par avis, il faisait tomber à zéro la note d'un produit ou d'une
    /// boutique. Le BFF vendeur exposait la même commande : un vendeur pouvait effacer
    /// les mauvais avis de SES PROPRES produits, et le contrôle de propriété
    /// (`OwnsReviewAsync`) l'y autorisait explicitement.
    ///
    /// Le défaut n'était pas l'absence de rôle sur la route, il était ICI. Signaler est
    /// un geste d'UTILISATEUR : tout le monde doit pouvoir dire « cet avis est abusif »,
    /// sinon les avis injurieux ne remontent jamais. Mais un geste ouvert à tous ne peut
    /// pas avoir un effet de MODÉRATION — sans quoi il suffit d'un clic pour censurer.
    ///
    /// `Flagged` signifie donc désormais « SIGNALÉ, EN ATTENTE DE REVUE » : l'avis reste
    /// public, il est seulement marqué. Seul `Rejected`, posé par `/reject` (réservé
    /// Admin/Modérateur), le retire des lectures publiques et déclenche le recalcul de
    /// la note vendeur. La décision de retirer redevient ce qu'elle doit être : humaine
    /// et autorisée.
    ///
    /// Liste blanche explicite plutôt que `!= Rejected` : un statut ajouté demain
    /// (brouillon, en attente d'achat vérifié) resterait invisible par défaut, ce qui
    /// est le sens le moins dangereux.
    /// ════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static IQueryable<Review> PubliclyVisible(IQueryable<Review> reviews)
        => reviews.Where(r => r.Status == ReviewStatus.Published || r.Status == ReviewStatus.Flagged);

    public async Task AddAsync(Review review, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews.AddAsync(review, cancellationToken);

    public async Task<Review?> GetByIdAsync(ReviewId id, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Review>> ListByProductAsync(Guid productId, CancellationToken cancellationToken = default)
        => await PubliclyVisible(_dbContext.Reviews)
            .Where(r => r.ProductId == productId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Review>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews
            .AsNoTracking()
            .Where(r => r.SellerId == sellerId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<bool> ExistsAsync(Guid buyerId, Guid productId, Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.Reviews.AnyAsync(
            r => r.BuyerId == buyerId && r.ProductId == productId && r.OrderId == orderId, cancellationToken);

    public async Task<ProductRating> GetProductRatingAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        // Rating est un Value Object converti : on agrège en mémoire pour éviter
        // toute traduction SQL fragile du VO. Volumétrie modérée par produit.
        var ratings = await PubliclyVisible(_dbContext.Reviews)
            .Where(r => r.ProductId == productId)
            .AsNoTracking()
            .Select(r => r.Rating)
            .ToListAsync(cancellationToken);

        if (ratings.Count == 0)
        {
            return new ProductRating(productId, 0d, 0);
        }

        return new ProductRating(productId, Math.Round(ratings.Average(r => r.Value), 2), ratings.Count);
    }

    public async Task<SellerRating> GetSellerRatingAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        // Même stratégie que la note produit : agrégation en mémoire du VO Rating,
        // sur TOUS les produits du vendeur. Volumétrie modérée par vendeur.
        var ratings = await PubliclyVisible(_dbContext.Reviews)
            .Where(r => r.SellerId == sellerId)
            .AsNoTracking()
            .Select(r => r.Rating)
            .ToListAsync(cancellationToken);

        if (ratings.Count == 0)
        {
            return new SellerRating(sellerId, 0d, 0);
        }

        return new SellerRating(sellerId, Math.Round(ratings.Average(r => r.Value), 2), ratings.Count);
    }
}
