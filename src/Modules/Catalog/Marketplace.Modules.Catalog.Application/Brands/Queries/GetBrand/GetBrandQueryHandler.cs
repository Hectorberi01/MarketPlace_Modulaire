using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Brands;

namespace Marketplace.Modules.Catalog.Application.Brands.Queries.GetBrand;

internal sealed class GetBrandQueryHandler : IQueryHandler<GetBrandQuery, BrandSummary>
{
    private readonly IBrandRepository _brandRepository;

    public GetBrandQueryHandler(IBrandRepository brandRepository)
        => _brandRepository = brandRepository;

    public async Task<Result<BrandSummary>> Handle(GetBrandQuery query, CancellationToken cancellationToken)
    {
        var brand = await _brandRepository.GetByIdAsync(new BrandId(query.BrandId), cancellationToken);

        if (brand is null)
        {
            return Error.NotFound("catalog.brand.not_found", $"Marque {query.BrandId} introuvable.");
        }

        return new BrandSummary(
            brand.Id.Value,
            brand.Name,
            brand.Slug.Value,
            brand.Status.ToString(),
            brand.LogoUrl,
            brand.Description);
    }
}
