using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.CreateProduct;

internal sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public CreateProductCommandHandler(IProductRepository productRepository, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {
        var result = Product.Create(
            command.SellerId,
            command.CategoryId,
            command.Name,
            command.Description,
            command.BrandId,
            command.Gtin,
            command.Ean,
            command.ProductGroupId,
            command.Attributes,
            command.Tags);

        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        var product = result.Value;

        if (await _productRepository.SlugExistsAsync(product.Slug.Value, cancellationToken))
        {
            return Error.Conflict("catalog.product.slug_taken", $"Le slug « {product.Slug.Value} » existe déjà.");
        }

        await _productRepository.AddAsync(product, cancellationToken);

        // SaveChanges dispatche le ProductCreatedDomainEvent, dont le handler
        // écrit l'IntegrationEvent dans l'outbox — le tout dans une transaction.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return product.Id.Value;
    }
}
