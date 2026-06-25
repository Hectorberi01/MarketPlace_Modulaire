using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.CreateProduct;

/// <summary>Crée un produit (Draft) et renvoie son identifiant.</summary>
public sealed record CreateProductCommand(
    Guid SellerId,
    Guid CategoryId,
    string Name,
    string Description,
    Guid? BrandId = null,
    string? Gtin = null,
    string? Ean = null,
    Guid? ProductGroupId = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    IReadOnlyList<string>? Tags = null) : ICommand<Guid>;
