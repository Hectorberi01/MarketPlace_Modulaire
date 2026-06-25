using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Sellers.Contracts.IntegrationEvents;

/// <summary>Un vendeur a été onboardé. Consommé par Notifications, Analytics…</summary>
public sealed record SellerRegisteredIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
    public required string ShopName { get; init; }
}

/// <summary>
/// Un vendeur est devenu actif : il peut publier des produits. Consommé par
/// Catalog (autorisation), Notifications (e-mail de bienvenue vendeur)…
/// </summary>
public sealed record SellerActivatedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
}
