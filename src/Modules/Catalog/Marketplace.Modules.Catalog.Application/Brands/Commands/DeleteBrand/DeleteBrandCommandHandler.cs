using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Brands;

namespace Marketplace.Modules.Catalog.Application.Brands.Commands.DeleteBrand;

/// <summary>Charge la marque puis la supprime ; renvoie NotFound si absente.</summary>
internal sealed class DeleteBrandCommandHandler : ICommandHandler<DeleteBrandCommand>
{
    private readonly IBrandRepository _brandRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public DeleteBrandCommandHandler(IBrandRepository brandRepository, ICatalogUnitOfWork unitOfWork)
    {
        _brandRepository = brandRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteBrandCommand command, CancellationToken cancellationToken)
    {
        var brand = await _brandRepository.GetByIdAsync(new BrandId(command.BrandId), cancellationToken);
        if (brand is null)
        {
            return Result.Failure(Error.NotFound("catalog.brand.not_found", $"Marque {command.BrandId} introuvable."));
        }

        _brandRepository.Remove(brand);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
