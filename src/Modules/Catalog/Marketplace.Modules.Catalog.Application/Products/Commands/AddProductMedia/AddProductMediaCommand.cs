using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.AddProductMedia;

public sealed record AddProductMediaCommand(
    Guid ProductId,
    string Url,
    string Type = "Image",
    string? AltText = null,
    bool IsPrimary = false,
    string? ExternalId = null) : ICommand<Guid>;
