using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Sellers.Domain.Sellers.Events;

/// <summary>Un vendeur vient d'être onboardé (statut Pending).</summary>
public sealed record SellerRegisteredDomainEvent(Guid SellerId, Guid UserId, string ShopName) : DomainEvent;

/// <summary>Le KYB d'un vendeur a été validé.</summary>
public sealed record SellerKybVerifiedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;

/// <summary>Un vendeur a été activé : il peut désormais publier des produits.</summary>
public sealed record SellerActivatedDomainEvent(Guid SellerId, Guid UserId) : DomainEvent;
