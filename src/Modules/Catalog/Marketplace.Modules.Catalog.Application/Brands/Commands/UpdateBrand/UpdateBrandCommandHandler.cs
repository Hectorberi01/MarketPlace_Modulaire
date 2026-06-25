using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Brands;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Brands.Commands.UpdateBrand;

/// <summary>Charge la marque, vérifie l'unicité du slug si le nom change, met à jour puis persiste.</summary>
internal sealed class UpdateBrandCommandHandler : ICommandHandler<UpdateBrandCommand>
{
    private readonly IBrandRepository _brandRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public UpdateBrandCommandHandler(IBrandRepository brandRepository, ICatalogUnitOfWork unitOfWork)
    {
        _brandRepository = brandRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateBrandCommand command, CancellationToken cancellationToken)
    {
        var brand = await _brandRepository.GetByIdAsync(new BrandId(command.BrandId), cancellationToken);
        if (brand is null)
        {
            return Result.Failure(Error.NotFound("catalog.brand.not_found", $"Marque {command.BrandId} introuvable."));
        }

        var slugResult = Slug.Create(command.Name);
        if (slugResult.IsFailure)
        {
            return Result.Failure(slugResult.Error);
        }

        var newSlug = slugResult.Value.Value;
        if (newSlug != brand.Slug.Value && await _brandRepository.SlugExistsAsync(newSlug, cancellationToken))
        {
            return Result.Failure(Error.Conflict("catalog.brand.slug_taken", $"La marque « {newSlug} » existe déjà."));
        }

        var updateResult = brand.Update(command.Name, command.LogoUrl, command.Description);
        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
