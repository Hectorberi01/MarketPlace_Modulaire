using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.UpdateProduct;

/// <summary>Met à jour les champs descriptifs d'un produit existant.</summary>
public sealed record UpdateProductCommand(
    Guid ProductId,
    string Name,
    string Description,
    Guid? BrandId = null,
    string? Gtin = null,
    string? Ean = null,
    Guid? ProductGroupId = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    IReadOnlyList<string>? Tags = null) : ICommand;
