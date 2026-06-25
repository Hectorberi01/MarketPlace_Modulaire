using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Catalog.Application.Brands.Commands.UpdateBrand;

/// <summary>Met à jour le nom, le logo et la description d'une marque.</summary>
public sealed record UpdateBrandCommand(
    Guid BrandId,
    string Name,
    string? LogoUrl = null,
    string? Description = null) : ICommand;
