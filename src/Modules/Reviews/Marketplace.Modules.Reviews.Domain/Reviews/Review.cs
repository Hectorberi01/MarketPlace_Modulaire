using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Reviews.Domain.Reviews.Events;

namespace Marketplace.Modules.Reviews.Domain.Reviews;

/// <summary>
/// Avis d'un acheteur sur un produit. Marqué « achat vérifié » lorsqu'il est
/// rattaché à une commande confirmée de l'acheteur. Publié à la création
/// (modération a posteriori : un admin peut le signaler ou le rejeter).
/// </summary>
public sealed class Review : AggregateRoot<ReviewId>
{
    private Review()
    {
    }

    private Review(
        ReviewId id, Guid productId, Guid sellerId, Guid buyerId, Guid orderId,
        Rating rating, string title, string body, bool isVerifiedPurchase)
        : base(id)
    {
        ProductId = productId;
        SellerId = sellerId;
        BuyerId = buyerId;
        OrderId = orderId;
        Rating = rating;
        Title = title;
        Body = body;
        IsVerifiedPurchase = isVerifiedPurchase;
        Status = ReviewStatus.Published;
        CreatedAtUtc = DateTime.UtcNow;

        Raise(new ReviewPublishedDomainEvent(id.Value, productId, rating.Value));
    }

    public Guid ProductId { get; private set; }
    public Guid SellerId { get; private set; }
    public Guid BuyerId { get; private set; }
    public Guid OrderId { get; private set; }
    public Rating Rating { get; private set; } = default!;
    public string Title { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public bool IsVerifiedPurchase { get; private set; }
    public ReviewStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Réponse publique du vendeur à l'avis (null tant qu'il n'a pas répondu).</summary>
    public string? SellerReply { get; private set; }
    public DateTime? SellerRepliedAtUtc { get; private set; }

    public static Result<Review> Create(
        Guid productId, Guid sellerId, Guid buyerId, Guid orderId,
        Rating rating, string title, string body, bool isVerifiedPurchase)
    {
        if (productId == Guid.Empty || buyerId == Guid.Empty)
        {
            return Error.Validation("reviews.refs_required", "Produit et acheteur sont obligatoires.");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Error.Validation("reviews.body_required", "Le contenu de l'avis est obligatoire.");
        }

        return new Review(
            ReviewId.New(), productId, sellerId, buyerId, orderId, rating,
            (title ?? string.Empty).Trim(), body.Trim(), isVerifiedPurchase);
    }

    /// <summary>
    /// Signale l'avis : le marque « en attente de revue », SANS le dépublier.
    ///
    /// ════════════════════════════════════════════════════════════════════════════
    /// SIGNALER N'EST PAS MODÉRER. C'EST TOUTE LA CORRECTION.
    ///
    /// Ce passage à `Flagged` retirait l'avis des lectures publiques, parce que les trois
    /// requêtes de `ReviewRepository` ne gardaient que `Published`. Comme le signalement
    /// est ouvert à tout compte authentifié (c'est voulu : chacun doit pouvoir remonter
    /// un avis injurieux), n'importe qui pouvait effacer un avis — et un vendeur pouvait
    /// effacer les siens, le BFF vendeur exposant la même commande.
    ///
    /// Un geste ouvert à tous ne peut avoir qu'un effet de SIGNAL, jamais de sanction.
    /// `Flagged` ne dit donc plus « retiré », il dit « quelqu'un conteste cet avis, un
    /// modérateur doit le regarder ». Le retrait effectif reste `Reject()`, réservé à
    /// Admin/Modérateur — et c'est lui, et lui seul, qui émet
    /// `ReviewRejectedDomainEvent` pour faire recalculer la note du vendeur. Signaler ne
    /// change aucune note : c'est précisément ce qu'on veut, sinon le signalement
    /// resterait une arme.
    ///
    /// Sans effet de retrait, l'opération est naturellement idempotente : signaler dix
    /// fois le même avis ne fait rien de plus que la première.
    /// ════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result Flag()
    {
        if (Status == ReviewStatus.Rejected)
        {
            return Result.Failure(Error.Conflict("reviews.rejected", "Un avis rejeté ne peut pas être signalé."));
        }

        Status = ReviewStatus.Flagged;
        return Result.Success();
    }

    /// <summary>
    /// Rejette l'avis (Admin / Modérateur) — SEUL geste qui le retire des lectures
    /// publiques et de la note agrégée. Contrairement à <see cref="Flag"/>, il est
    /// autorisé au niveau de la route (`/api/reviews/{id}/reject`).
    /// </summary>
    public Result Reject()
    {
        if (Status == ReviewStatus.Rejected)
        {
            return Result.Failure(Error.Conflict("reviews.already_rejected", "L'avis est déjà rejeté."));
        }

        Status = ReviewStatus.Rejected;
        Raise(new ReviewRejectedDomainEvent(Id.Value, ProductId));
        return Result.Success();
    }

    /// <summary>Restaure un avis signalé/rejeté.</summary>
    public Result Restore()
    {
        if (Status == ReviewStatus.Published)
        {
            return Result.Failure(Error.Conflict("reviews.already_published", "L'avis est déjà publié."));
        }

        Status = ReviewStatus.Published;
        Raise(new ReviewPublishedDomainEvent(Id.Value, ProductId, Rating.Value));
        return Result.Success();
    }

    /// <summary>Réponse publique du vendeur à l'avis (remplace une réponse existante).</summary>
    public Result Reply(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Result.Failure(Error.Validation("reviews.reply_required", "La réponse ne peut pas être vide."));
        }

        SellerReply = body.Trim();
        SellerRepliedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }
}
