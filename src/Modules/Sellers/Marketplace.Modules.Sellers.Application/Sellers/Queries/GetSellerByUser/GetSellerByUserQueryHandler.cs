using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Application.Sellers.Queries.GetSellerByUser;

internal sealed class GetSellerByUserQueryHandler : IQueryHandler<GetSellerByUserQuery, SellerSummary>
{
    private readonly ISellerRepository _sellerRepository;

    public GetSellerByUserQueryHandler(ISellerRepository sellerRepository)
        => _sellerRepository = sellerRepository;

    public async Task<Result<SellerSummary>> Handle(GetSellerByUserQuery query, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByUserIdAsync(query.UserId, cancellationToken);
        if (seller is null)
        {
            return Error.NotFound("sellers.seller.not_found", "Aucune boutique pour ce compte.");
        }

        return SellerMapper.ToSummary(seller);
    }
}
