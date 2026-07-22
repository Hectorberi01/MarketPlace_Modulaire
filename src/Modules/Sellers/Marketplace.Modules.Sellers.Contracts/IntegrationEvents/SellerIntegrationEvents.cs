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

/// <summary>
/// Le vendeur a fermé son compte (suppression partielle). Consommé par Catalog
/// (retrait des produits de la vente), Notifications…
/// </summary>
public sealed record SellerClosedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
}

/// <summary>Le compte fermé du vendeur a été réactivé (validation admin).</summary>
public sealed record SellerReactivatedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
}

/// <summary>
/// Le vendeur est supprimé définitivement (admin). Consommé par Catalog (purge :
/// archivage de tous ses produits).
/// </summary>
public sealed record SellerDeletedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
}
