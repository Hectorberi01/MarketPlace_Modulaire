using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.Modules.Catalog.Contracts.IntegrationEvents;

/// <summary>
/// Publié quand les champs descriptifs d'un produit changent (nom, slug, catégorie,
/// marque, attributs, tags). Consommé par Search pour réindexer : sans lui, l'index
/// gardait le nom du jour de la création.
/// </summary>
public sealed record ProductUpdatedIntegrationEvent : IntegrationEvent
{
    public required Guid ProductId { get; init; }
    public required Guid SellerId { get; init; }
    public required Guid CategoryId { get; init; }
    public required string Name { get; init; }
    public required string Slug { get; init; }
}
