using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Catalog.Contracts.IntegrationEvents;

/// <summary>
/// Publié quand le statut d'un produit change. Consommé par Search pour gérer la
/// visibilité publique : seul un produit « Active » (validé) apparaît en vitrine.
/// </summary>
public sealed record ProductStatusChangedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required string Status { get; init; }
}
