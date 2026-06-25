using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.Modules.Catalog.Contracts;

namespace Marketplace.Modules.Catalog.Application.Products.Queries.GetProduct;

/// <summary>Lecture d'un produit par son identifiant.</summary>
public sealed record GetProductQuery(Guid ProductId) : IQuery<ProductSummary>;
