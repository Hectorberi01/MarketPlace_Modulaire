using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.AddProductMedia;

internal sealed class AddProductMediaCommandHandler : ICommandHandler<AddProductMediaCommand, Guid>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public AddProductMediaCommandHandler(IProductRepository productRepository, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(AddProductMediaCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable.");
        }

        if (!Enum.TryParse<ProductMediaType>(command.Type, ignoreCase: true, out var mediaType))
        {
            return Error.Validation("catalog.media.type_invalid", "Type de média invalide (Image ou Video).");
        }

        var result = product.AddMedia(command.Url, mediaType, command.AltText, command.IsPrimary, command.ExternalId);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result.Value.Id;
    }
}
