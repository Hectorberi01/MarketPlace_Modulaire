using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Sellers.Domain.Sellers.Events;

/// <summary>Un vendeur vient d'être onboardé (statut Pending).</summary>
public sealed record SellerRegisteredDomainEvent(Guid SellerId, Guid UserId, string ShopName) : DomainEvent;

/// <summary>Le KYB d'un vendeur a été validé.</summary>
public sealed record SellerKybVerifiedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>Un vendeur a été activé : il peut désormais publier des produits.</summary>
public sealed record SellerActivatedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>Le vendeur a fermé son compte (suppression partielle) : ses produits doivent être retirés de la vente.</summary>
public sealed record SellerClosedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>Le compte fermé du vendeur a été réactivé (validation admin).</summary>
public sealed record SellerReactivatedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>Le vendeur est supprimé définitivement (admin) : purge de ses produits.</summary>
public sealed record SellerDeletedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;
