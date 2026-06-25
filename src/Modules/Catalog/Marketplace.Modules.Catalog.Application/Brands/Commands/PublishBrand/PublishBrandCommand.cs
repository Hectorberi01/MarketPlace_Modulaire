using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Catalog.Application.Brands.Commands.PublishBrand;

/// <summary>Publie (approuve) une marque après modération : Pending -> Active.</summary>
public sealed record PublishBrandCommand(Guid BrandId) : ICommand;
