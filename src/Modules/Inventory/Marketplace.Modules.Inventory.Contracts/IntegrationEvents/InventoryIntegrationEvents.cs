using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Inventory.Contracts.IntegrationEvents;

/// <summary>Du stock a été réservé pour une commande (consommé par Ordering).</summary>
public sealed record StockReservedIntegrationEvent : IntegrationEvent
{
    public required Guid InventoryItemId { get; init; }
    public required string Sku { get; init; }
    public required Guid OrderId { get; init; }
    public required int Quantity { get; init; }
}

/// <summary>
/// Un SKU est en rupture.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// CE RÉSUMÉ ANNONÇAIT UN ABONNÉ QUI N'EXISTE PAS. Il disait « consommé par Offers
/// pour passer l'offre OutOfStock » : le SEUL abonné est Notifications. Il n'y a aucun
/// handler dans Offers. `Offer.MarkOutOfStock()` n'est atteignable que par un
/// `PATCH .../status` portant « OutOfStock » — deux routes l'exposent, celle du BFF
/// vendeur et celle du back-office admin — et AUCUN client n'envoie jamais ce statut :
/// la console vendeur et l'app mobile ne proposent que « Active » et « Paused ».
///
/// CONSÉQUENCE : AUCUNE OFFRE NE PASSE JAMAIS « EN RUPTURE ». Une offre reste
/// « Active » à zéro disponible, et le statut correspondant est du code mort côté
/// affichage. Ce qui protège l'acheteur est ailleurs — `IsInStockAsync` au panier et la
/// réservation à la commande — mais l'offre continue de se présenter comme disponible
/// jusqu'à cet instant.
///
/// La correction est un choix de produit, pas une ligne de code : délister
/// automatiquement à zéro est un comportement visible pour le vendeur, qui doit être
/// décidé (et probablement accompagné d'un retour en « Active » au réassort). En
/// attendant, ce commentaire dit ce qui EST, plutôt qu'une intention.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record StockDepletedIntegrationEvent : IntegrationEvent
{
    public required Guid InventoryItemId { get; init; }
    public required string Sku { get; init; }
    public required Guid LocationId { get; init; }
}
