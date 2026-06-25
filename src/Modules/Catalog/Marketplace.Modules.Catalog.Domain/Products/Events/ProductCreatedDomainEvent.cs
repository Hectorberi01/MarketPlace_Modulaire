using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Catalog.Domain.Products.Events;

/// <summary>
/// Fait de domaine : un produit vient d'être créé. Reste DANS le module ; un
/// handler le traduira en IntegrationEvent pour Search, Inventory, etc.
/// </summary>
public sealed record ProductCreatedDomainEvent(
    Guid ProductId,
    Guid SellerId,
    Guid CategoryId,
    string Name,
    string Slug) : DomainEvent;
