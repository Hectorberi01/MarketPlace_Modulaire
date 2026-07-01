using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Catalog.Application.Brands.Commands.UnpublishBrand;

/// <summary>Dépublie une marque (Active -> Pending), la retirant du catalogue actif.</summary>
public sealed record UnpublishBrandCommand(Guid BrandId) : ICommand;
