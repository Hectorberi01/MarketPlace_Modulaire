using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Brands;

namespace Marketplace.Modules.Catalog.Application.Brands.Queries.ListBrands;

/// <summary>Liste toutes les marques de la plateforme (filtres, back-office admin).</summary>
public sealed record ListBrandsQuery : IQuery<IReadOnlyList<BrandSummary>>;

internal sealed class ListBrandsQueryHandler : IQueryHandler<ListBrandsQuery, IReadOnlyList<BrandSummary>>
{
    private readonly IBrandRepository _brandRepository;
    private readonly ICacheService _cache;

    public ListBrandsQueryHandler(IBrandRepository brandRepository, ICacheService cache)
    {
        _brandRepository = brandRepository;
        _cache = cache;
    }

    public async Task<Result<IReadOnlyList<BrandSummary>>> Handle(ListBrandsQuery query, CancellationToken cancellationToken)
    {
        // Donnée de référence, comme les catégories : lue sans cesse, écrite presque
        // jamais.
        var summaries = await _cache.GetOrCreateAsync(
            CatalogCacheKeys.AllBrands,
            async ct =>
            {
                var brands = await _brandRepository.ListAllAsync(ct);
                return brands
                    .Select(b => new BrandSummary(b.Id.Value, b.Name, b.Slug.Value, b.Status.ToString(), b.LogoUrl, b.Description))
                    .ToList();
            },
            CatalogCacheKeys.ReferenceDataTtl,
            cancellationToken: cancellationToken);

        IReadOnlyList<BrandSummary> result = summaries ?? [];
        return Result.Success(result);
    }
}
