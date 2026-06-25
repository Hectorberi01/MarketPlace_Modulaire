using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Brands;

namespace Marketplace.Modules.Catalog.Application.Brands.Commands.PublishBrand;

/// <summary>Charge la marque et applique la transition d'approbation du domaine.</summary>
internal sealed class PublishBrandCommandHandler : ICommandHandler<PublishBrandCommand>
{
    private readonly IBrandRepository _brandRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public PublishBrandCommandHandler(IBrandRepository brandRepository, ICatalogUnitOfWork unitOfWork)
    {
        _brandRepository = brandRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(PublishBrandCommand command, CancellationToken cancellationToken)
    {
        var brand = await _brandRepository.GetByIdAsync(new BrandId(command.BrandId), cancellationToken);
        if (brand is null)
        {
            return Result.Failure(Error.NotFound("catalog.brand.not_found", $"Marque {command.BrandId} introuvable."));
        }

        var result = brand.Approve();
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
