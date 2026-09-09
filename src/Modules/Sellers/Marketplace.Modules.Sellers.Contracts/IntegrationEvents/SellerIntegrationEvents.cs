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

/// <summary>
/// Le KYB du vendeur a été rejeté. Consommé par Notifications : sans lui, le vendeur
/// n'apprenait jamais le refus ni sa raison, et re-téléversait la pièce refusée.
/// </summary>
public sealed record SellerKybRejectedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }
    public required string Reason { get; init; }
}

/// <summary>
/// Le vendeur a été SUSPENDU par un administrateur. Consommé par Catalog (retrait de
/// ses produits de la vitrine) et Notifications (le vendeur doit apprendre la sanction
/// et son motif autrement que par la chute de ses ventes).
/// </summary>
public sealed record SellerSuspendedIntegrationEvent : IntegrationEvent
{
    public required Guid SellerId { get; init; }
    public required Guid UserId { get; init; }

    /// <summary>Motif communiqué au vendeur. Null si l'administrateur n'en a pas donné.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// La suspension a été levée. Consommé par Notifications. Catalog ne REPUBLIE PAS de
/// lui-même : une dépublication administrative se défait produit par produit, par le
/// vendeur, faute de quoi une fiche qu'il avait retirée lui-même avant la sanction
/// ressusciterait avec les autres.
/// </summary>
public sealed record SellerSuspensionLiftedIntegrationEvent : IntegrationEvent
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
