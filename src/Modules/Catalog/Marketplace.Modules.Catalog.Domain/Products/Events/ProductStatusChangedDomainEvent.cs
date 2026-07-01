using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Catalog.Domain.Products.Events;

/// <summary>
/// Fait de domaine : le statut d'un produit a changé (Draft / Active / Archived).
/// Un handler le traduit en IntegrationEvent ; le module Search s'en sert pour
/// rendre le produit visible (Active) ou le masquer (Draft / Archived) dans la
/// vitrine publique.
/// </summary>
public sealed record ProductStatusChangedDomainEvent(
    Guid ProductId,
    string Status) : DomainEvent;
