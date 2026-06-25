using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Brands;

namespace Marketplace.Modules.Catalog.Application.Brands.Commands.CreateBrand;

internal sealed class CreateBrandCommandHandler : ICommandHandler<CreateBrandCommand, Guid>
{
    private readonly IBrandRepository _brandRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public CreateBrandCommandHandler(IBrandRepository brandRepository, ICatalogUnitOfWork unitOfWork)
    {
        _brandRepository = brandRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateBrandCommand command, CancellationToken cancellationToken)
    {
        var result = Brand.Create(command.Name, command.LogoUrl, command.Description);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        var brand = result.Value;

        if (await _brandRepository.SlugExistsAsync(brand.Slug.Value, cancellationToken))
        {
            return Error.Conflict("catalog.brand.slug_taken", $"La marque « {brand.Slug.Value} » existe déjà.");
        }

        await _brandRepository.AddAsync(brand, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return brand.Id.Value;
    }
}
