using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Brands;

namespace Marketplace.Modules.Catalog.Application.Brands.Queries.ListBrands;

/// <summary>Liste toutes les marques de la plateforme (back-office admin).</summary>
public sealed record ListBrandsQuery : IQuery<IReadOnlyList<BrandSummary>>;

internal sealed class ListBrandsQueryHandler : IQueryHandler<ListBrandsQuery, IReadOnlyList<BrandSummary>>
{
    private readonly IBrandRepository _brandRepository;

    public ListBrandsQueryHandler(IBrandRepository brandRepository) => _brandRepository = brandRepository;

    public async Task<Result<IReadOnlyList<BrandSummary>>> Handle(ListBrandsQuery query, CancellationToken cancellationToken)
    {
        var brands = await _brandRepository.ListAllAsync(cancellationToken);
        IReadOnlyList<BrandSummary> summaries = brands
            .Select(b => new BrandSummary(b.Id.Value, b.Name, b.Slug.Value, b.Status.ToString(), b.LogoUrl, b.Description))
            .ToList();
        return Result.Success(summaries);
    }
}
