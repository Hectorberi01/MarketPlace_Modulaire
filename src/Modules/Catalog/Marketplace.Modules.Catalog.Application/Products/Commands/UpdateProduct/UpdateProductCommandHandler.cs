using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.UpdateProduct;

/// <summary>
/// Charge le produit, vérifie l'unicité du slug si le nom a changé, applique la
/// mise à jour métier puis persiste.
/// </summary>
internal sealed class UpdateProductCommandHandler : ICommandHandler<UpdateProductCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public UpdateProductCommandHandler(IProductRepository productRepository, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Result.Failure(Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable."));
        }

        var slugResult = Slug.Create(command.Name);
        if (slugResult.IsFailure)
        {
            return Result.Failure(slugResult.Error);
        }

        var newSlug = slugResult.Value.Value;
        if (newSlug != product.Slug.Value && await _productRepository.SlugExistsAsync(newSlug, cancellationToken))
        {
            return Result.Failure(Error.Conflict("catalog.product.slug_taken", $"Le slug « {newSlug} » existe déjà."));
        }

        var updateResult = product.Update(
            command.Name,
            command.Description,
            command.BrandId,
            command.Gtin,
            command.Ean,
            command.ProductGroupId,
            command.Attributes,
            command.Tags);

        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
