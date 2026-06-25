using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Application.Sellers.Queries.ListSellers;

internal sealed class ListSellersQueryHandler : IQueryHandler<ListSellersQuery, IReadOnlyList<SellerSummary>>
{
    private readonly ISellerRepository _sellerRepository;

    public ListSellersQueryHandler(ISellerRepository sellerRepository)
        => _sellerRepository = sellerRepository;

    public async Task<Result<IReadOnlyList<SellerSummary>>> Handle(ListSellersQuery query, CancellationToken cancellationToken)
    {
        var sellers = await _sellerRepository.ListAsync(cancellationToken);

        IReadOnlyList<SellerSummary> summaries = sellers.Select(SellerMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}
