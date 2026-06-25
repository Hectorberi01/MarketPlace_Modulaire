using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Catalog.Application.Brands.Commands.DeleteBrand;

/// <summary>Supprime une marque. Les produits qui la référençaient ne sont pas modifiés (référence souple).</summary>
public sealed record DeleteBrandCommand(Guid BrandId) : ICommand;
