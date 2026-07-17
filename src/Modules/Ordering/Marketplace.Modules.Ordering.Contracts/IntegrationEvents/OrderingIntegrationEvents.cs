using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Ordering.Contracts.IntegrationEvents;

/// <summary>Commande placée (stock réservé, en attente de paiement). Consommé par Cart (clôture) / Payments.</summary>
public sealed record OrderPlacedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required Guid CartId { get; init; }
    public required decimal GrandTotal { get; init; }
    public required string Currency { get; init; }
}

/// <summary>
/// Part d'UN vendeur dans une commande : ce qu'il a vendu, et pour combien.
///
/// Une commande peut réunir les produits de PLUSIEURS vendeurs. Chacun ne doit voir
/// que sa part : lui communiquer le total de la commande lui ferait croire qu'il a
/// vendu pour le montant d'un autre — et révélerait au passage le chiffre d'un
/// concurrent.
/// </summary>
/// <param name="SellerId">Le vendeur concerné.</param>
/// <param name="ItemCount">Nombre d'articles de CE vendeur.</param>
/// <param name="Amount">Montant acheté chez CE vendeur (prix final, remises comprises,
/// AVANT commission de la plateforme).</param>
public sealed record OrderSellerShare(Guid SellerId, int ItemCount, decimal Amount);

/// <summary>
/// Commande confirmée (paiement encaissé). Consommé par Shipping / Notifications /
/// Settlement / Loyalty / Analytics.
///
/// Porte désormais la RÉPARTITION PAR VENDEUR. Sans elle, le module Notifications
/// était incapable de prévenir les vendeurs d'une commande : il ne recevait que
/// l'acheteur et l'identifiant de commande — et c'est exactement pourquoi les
/// vendeurs n'étaient jamais notifiés.
/// </summary>
public sealed record OrderConfirmedIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required string Currency { get; init; }

    /// <summary>
    /// Code promo utilisé par cette commande, ou null. Consommé par Pricing, qui décompte
    /// la promotion À CET INSTANT — pas avant.
    ///
    /// Pourquoi à la confirmation et pas au checkout : une commande peut être placée puis
    /// échouer au paiement. Décompter au checkout « brûlerait » un coupon pour une vente
    /// qui n'a jamais eu lieu, et il faudrait le rendre en compensation — une saga de plus,
    /// pour rien. Ici, seule une vente réellement encaissée consomme le coupon.
    /// </summary>
    public string? PromotionCode { get; init; }

    /// <summary>Les vendeurs concernés, et la part de chacun. Jamais vide en pratique.</summary>
    public required IReadOnlyCollection<OrderSellerShare> SellerShares { get; init; }
}

/// <summary>Commande annulée. Consommé par Notifications / analytics.</summary>
public sealed record OrderCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
    public required string Reason { get; init; }
}

/// <summary>Commande livrée. Consommé par Payments (libération escrow) et Settlement (payout).</summary>
public sealed record OrderDeliveredIntegrationEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid BuyerId { get; init; }
}
