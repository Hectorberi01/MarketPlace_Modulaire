using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Pricing.Domain.Promotions;

/// <summary>
/// Trace d'une promotion RÉELLEMENT consommée par une commande confirmée.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CETTE TABLE EXISTE.
///
/// Avant, l'usage d'une promotion n'était écrit NULLE PART. `Promotion.UsedCount`
/// restait à 0 pour l'éternité, `RecordUsage()` n'avait aucun appelant, et donc :
///   • une promo « 100 premiers clients » servait 100 000 clients ;
///   • un coupon « une fois par personne » n'existait même pas comme notion.
///
/// Un compteur seul (`UsedCount++`) n'aurait pas suffi. Il ne dit pas QUI a consommé,
/// donc pas de limite par personne ; et il ne dit pas POUR QUELLE COMMANDE, donc aucun
/// moyen de savoir si un rejeu d'outbox a déjà été traité. Or l'outbox est
/// **at-least-once** : le même `OrderConfirmedIntegrationEvent` PEUT être livré deux
/// fois. Sans trace, le second passage décompterait une seconde fois le coupon d'une
/// commande unique.
///
/// D'où une LIGNE par (promotion, commande), et un index unique dessus. C'est la base
/// — pas le code applicatif — qui garantit l'unicité, parce qu'entre un `SELECT` de
/// vérification et l'`INSERT`, deux rejeux concurrents peuvent tous deux se croire les
/// premiers. Même leçon que l'index d'idempotence des remboursements.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class PromotionRedemption : Entity<Guid>
{
    // Requis par EF (matérialisation).
    private PromotionRedemption()
    {
    }

    private PromotionRedemption(Guid id, PromotionId promotionId, Guid buyerId, Guid orderId, DateTime redeemedAtUtc)
        : base(id)
    {
        PromotionId = promotionId;
        BuyerId = buyerId;
        OrderId = orderId;
        RedeemedAtUtc = redeemedAtUtc;
    }

    public PromotionId PromotionId { get; private set; }
    public Guid BuyerId { get; private set; }
    public Guid OrderId { get; private set; }
    public DateTime RedeemedAtUtc { get; private set; }

    public static PromotionRedemption For(PromotionId promotionId, Guid buyerId, Guid orderId, DateTime nowUtc)
        => new(Guid.NewGuid(), promotionId, buyerId, orderId, nowUtc);
}
