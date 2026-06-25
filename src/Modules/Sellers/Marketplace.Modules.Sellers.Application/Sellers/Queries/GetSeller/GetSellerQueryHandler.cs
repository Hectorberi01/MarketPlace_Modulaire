using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Application.Sellers.Queries.GetSeller;

internal sealed class GetSellerQueryHandler : IQueryHandler<GetSellerQuery, SellerSummary>
{
    private readonly ISellerRepository _sellerRepository;

    public GetSellerQueryHandler(ISellerRepository sellerRepository)
        => _sellerRepository = sellerRepository;

    public async Task<Result<SellerSummary>> Handle(GetSellerQuery query, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByIdAsync(new SellerId(query.SellerId), cancellationToken);
        if (seller is null)
        {
            return Error.NotFound("sellers.seller.not_found", $"Vendeur {query.SellerId} introuvable.");
        }

        return SellerMapper.ToSummary(seller);
    }
}
