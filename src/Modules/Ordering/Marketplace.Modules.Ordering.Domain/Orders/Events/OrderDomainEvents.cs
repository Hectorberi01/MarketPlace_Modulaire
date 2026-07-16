using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Ordering.Domain.Orders.Events;

/// <summary>
/// Part d'UN vendeur dans une commande : ce qu'il a vendu, et pour combien.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POURQUOI CE TYPE EXISTE
///
/// Une commande peut contenir les produits de PLUSIEURS vendeurs. Chacun ne doit
/// connaître que SA part.
///
/// Envoyer à un vendeur le total de la commande — qui inclut les articles des
/// autres — lui ferait croire qu'il a vendu pour un montant qui n'est pas le sien.
/// Il l'apprendrait au moment d'être payé, et la confiance ne s'en remettrait pas.
/// C'est aussi, accessoirement, une fuite d'information commerciale entre
/// concurrents.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
/// <param name="SellerId">Le vendeur concerné.</param>
/// <param name="ItemCount">Nombre d'articles de CE vendeur (somme des quantités).</param>
/// <param name="Amount">Montant dû à CE vendeur pour cette commande.</param>
public sealed record OrderSellerShare(Guid SellerId, int ItemCount, decimal Amount);

/// <summary>Une commande a été placée (stock réservé, en attente de paiement).</summary>
public sealed record OrderPlacedDomainEvent(Guid OrderId, Guid BuyerId, Guid CartId, decimal GrandTotal, string Currency) : DomainEvent;

/// <summary>
/// La commande a été confirmée (paiement encaissé, stock soldé).
///
/// C'est LE moment où les vendeurs sont prévenus : la commande est payée, donc
/// réelle. Prévenir plus tôt (à « OrderPlaced ») reviendrait à les alerter pour des
/// paniers dont le paiement peut encore échouer — et à les pousser à préparer, voire
/// expédier, une marchandise jamais payée.
///
/// L'événement transporte donc la répartition par vendeur : sans elle, aucun
/// consommateur en aval ne pourrait savoir QUI prévenir, ni de QUOI.
/// </summary>
public sealed record OrderConfirmedDomainEvent(
    Guid OrderId,
    Guid BuyerId,
    string Currency,
    string? PromotionCode,
    IReadOnlyCollection<OrderSellerShare> SellerShares) : DomainEvent;

/// <summary>La commande a été annulée (réservations libérées).</summary>
public sealed record OrderCancelledDomainEvent(Guid OrderId, Guid BuyerId, string Reason) : DomainEvent;

/// <summary>La commande a été livrée (escrow à libérer, payout vendeur à déclencher).</summary>
public sealed record OrderDeliveredDomainEvent(Guid OrderId, Guid BuyerId) : DomainEvent;
