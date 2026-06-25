using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Wishlist.Contracts;
using Marketplace.Modules.Wishlist.Domain.Wishlists;

namespace Marketplace.Modules.Wishlist.Application.Wishlists;

/// <summary>Récupère la liste d'envies de l'utilisateur (vide si inexistante).</summary>
public sealed record GetMyWishlistQuery(Guid UserId) : IQuery<WishlistSummary>;

internal sealed class GetMyWishlistQueryHandler : IQueryHandler<GetMyWishlistQuery, WishlistSummary>
{
    private readonly IWishlistRepository _repository;

    public GetMyWishlistQueryHandler(IWishlistRepository repository) => _repository = repository;

    public async Task<Result<WishlistSummary>> Handle(GetMyWishlistQuery query, CancellationToken cancellationToken)
    {
        var wishlist = await _repository.GetByUserAsync(query.UserId, cancellationToken);
        if (wishlist is null)
        {
            return new WishlistSummary(Guid.Empty, query.UserId, Array.Empty<WishlistItemSummary>());
        }

        var items = wishlist.Items
            .OrderByDescending(i => i.AddedAtUtc)
            .Select(i => new WishlistItemSummary(i.ProductId, i.OfferId, i.PriceAlert, i.StockAlert, i.AddedAtUtc))
            .ToList();

        return new WishlistSummary(wishlist.Id.Value, wishlist.UserId, items);
    }
}
