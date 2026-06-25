using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Inventory.Domain.Stock.Events;

/// <summary>Un article de stock a été créé pour un SKU sur une localisation.</summary>
public sealed record InventoryItemCreatedDomainEvent(Guid InventoryItemId, string Sku, Guid LocationId) : DomainEvent;

/// <summary>Du stock a été réservé pour une commande.</summary>
public sealed record StockReservedDomainEvent(Guid InventoryItemId, string Sku, Guid OrderId, int Quantity) : DomainEvent;

/// <summary>Le stock disponible d'un SKU est tombé à zéro (rupture).</summary>
public sealed record StockDepletedDomainEvent(Guid InventoryItemId, string Sku, Guid LocationId) : DomainEvent;
