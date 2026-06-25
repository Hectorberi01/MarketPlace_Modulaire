using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.ChangeProductStatus;

/// <summary>
/// Route le statut cible vers la transition métier correspondante de l'agrégat
/// (Activate / Archive / Unarchive) ; les invariants restent dans le domaine.
/// </summary>
internal sealed class ChangeProductStatusCommandHandler : ICommandHandler<ChangeProductStatusCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public ChangeProductStatusCommandHandler(IProductRepository productRepository, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ChangeProductStatusCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Result.Failure(Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable."));
        }

        if (!Enum.TryParse<ProductStatus>(command.Status, ignoreCase: true, out var target))
        {
            return Result.Failure(Error.Validation("catalog.product.status_invalid", "Statut invalide (Active, Archived ou Draft)."));
        }

        var transition = target switch
        {
            ProductStatus.Active => product.Activate(),
            ProductStatus.Archived => product.Archive(),
            ProductStatus.Draft => product.Unarchive(),
            _ => Result.Failure(Error.Validation("catalog.product.status_invalid", "Statut invalide."))
        };

        if (transition.IsFailure)
        {
            return transition;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
