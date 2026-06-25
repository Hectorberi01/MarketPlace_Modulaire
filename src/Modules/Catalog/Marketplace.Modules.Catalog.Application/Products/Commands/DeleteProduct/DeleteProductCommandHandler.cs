using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.DeleteProduct;

/// <summary>Charge le produit puis le supprime ; renvoie NotFound s'il n'existe pas.</summary>
internal sealed class DeleteProductCommandHandler : ICommandHandler<DeleteProductCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public DeleteProductCommandHandler(IProductRepository productRepository, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteProductCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Result.Failure(Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable."));
        }

        _productRepository.Remove(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
