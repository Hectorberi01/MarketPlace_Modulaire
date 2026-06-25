using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Catalog.Application.Brands.Commands.CreateBrand;

public sealed record CreateBrandCommand(
    string Name,
    string? LogoUrl = null,
    string? Description = null) : ICommand<Guid>;
