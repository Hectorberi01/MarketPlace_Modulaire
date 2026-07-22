using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.AddProductVariant;

internal sealed class AddProductVariantCommandHandler : ICommandHandler<AddProductVariantCommand, Guid>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public AddProductVariantCommandHandler(IProductRepository productRepository, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(AddProductVariantCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable.");
        }

        // SKU laissé vide par l'app : on le génère à partir de l'ID vendeur du
        // produit (le vendeur n'a pas à inventer une référence unique). S'il en a
        // saisi un, on le respecte.
        var sku = string.IsNullOrWhiteSpace(command.Sku)
            ? Sku.Generate(product.SellerId).Value
            : command.Sku;

        var result = product.AddVariant(
            sku,
            command.Attributes,
            command.Barcode,
            command.WeightGrams,
            command.LengthMm,
            command.WidthMm,
            command.HeightMm);

        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result.Value.Id;
    }
}
