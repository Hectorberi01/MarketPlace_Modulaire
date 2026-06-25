using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.AddProductVariant;

public sealed record AddProductVariantCommand(
    Guid ProductId,
    string Sku,
    IReadOnlyDictionary<string, string>? Attributes = null,
    string? Barcode = null,
    int WeightGrams = 0,
    int? LengthMm = null,
    int? WidthMm = null,
    int? HeightMm = null) : ICommand<Guid>;
