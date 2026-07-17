using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Brands;

namespace Marketplace.Modules.Catalog.Application.Brands.Queries.GetBrand;

internal sealed class GetBrandQueryHandler : IQueryHandler<GetBrandQuery, BrandSummary>
{
    private readonly IBrandRepository _brandRepository;
    private readonly ICacheService _cache;

    public GetBrandQueryHandler(IBrandRepository brandRepository, ICacheService cache)
    {
        _brandRepository = brandRepository;
        _cache = cache;
    }

    public async Task<Result<BrandSummary>> Handle(GetBrandQuery query, CancellationToken cancellationToken)
    {
        var summary = await _cache.GetOrCreateAsync(
            CatalogCacheKeys.Brand(query.BrandId),
            async ct =>
            {
                var brand = await _brandRepository.GetByIdAsync(new BrandId(query.BrandId), ct);
                return brand is null
                    ? null
                    : new BrandSummary(
                        brand.Id.Value,
                        brand.Name,
                        brand.Slug.Value,
                        brand.Status.ToString(),
                        brand.LogoUrl,
                        brand.Description);
            },
            CatalogCacheKeys.ReferenceDataTtl,
            CatalogCacheKeys.MissTtl,
            cancellationToken);

        if (summary is null)
        {
            return Error.NotFound("catalog.brand.not_found", $"Marque {query.BrandId} introuvable.");
        }

        return summary;
    }
}
