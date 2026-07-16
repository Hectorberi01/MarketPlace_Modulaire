using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Cart.Contracts;
using Marketplace.Modules.Cart.Domain.Carts;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Pricing.Contracts;

namespace Marketplace.Modules.Cart.Application.Carts.Queries;

/// <summary>Récupère le panier actif valorisé de l'acheteur.</summary>
public sealed record GetActiveCartQuery(Guid BuyerId) : IQuery<CartSummary>;

/// <summary>Récupère un panier valorisé par son identifiant.</summary>
public sealed record GetCartByIdQuery(Guid CartId) : IQuery<CartSummary>;

internal sealed class GetActiveCartQueryHandler : IQueryHandler<GetActiveCartQuery, CartSummary>
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    private readonly ICartRepository _cartRepository;
    private readonly IPricingModuleApi _pricing;
    private readonly IOrderingModuleApi _ordering;
    private readonly ICacheService _cache;

    public GetActiveCartQueryHandler(
        ICartRepository cartRepository, IPricingModuleApi pricing, IOrderingModuleApi ordering, ICacheService cache)
    {
        _cartRepository = cartRepository;
        _pricing = pricing;
        _ordering = ordering;
        _cache = cache;
    }

    public async Task<Result<CartSummary>> Handle(GetActiveCartQuery query, CancellationToken cancellationToken)
    {
        // Cache-aside : le panier valorisé est une lecture chaude. Invalidé à chaque
        // mutation du panier (read-your-writes garanti côté commandes).
        var key = CartCacheKeys.Active(query.BuyerId);
        var cached = await _cache.GetAsync<CartSummary>(key, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var cart = await _cartRepository.GetActiveByBuyerAsync(query.BuyerId, cancellationToken);
        if (cart is null)
        {
            // Panier inexistant : on renvoie un panier vide (et non une erreur),
            // pour que le client affiche un panier vide plutôt qu'un échec.
            return new CartSummary(
                Guid.Empty, query.BuyerId, "XOF", "Active",
                Array.Empty<CartLineSummary>(), 0m, 0m, 0m, 0m);
        }

        var summary = await CartPricer.PriceAsync(cart, _pricing, _ordering, cancellationToken);
        await _cache.SetAsync(key, summary, CacheTtl, cancellationToken);
        return summary;
    }
}

internal sealed class GetCartByIdQueryHandler : IQueryHandler<GetCartByIdQuery, CartSummary>
{
    private readonly ICartRepository _cartRepository;
    private readonly IPricingModuleApi _pricing;
    private readonly IOrderingModuleApi _ordering;

    public GetCartByIdQueryHandler(ICartRepository cartRepository, IPricingModuleApi pricing, IOrderingModuleApi ordering)
    {
        _cartRepository = cartRepository;
        _pricing = pricing;
        _ordering = ordering;
    }

    public async Task<Result<CartSummary>> Handle(GetCartByIdQuery query, CancellationToken cancellationToken)
    {
        var cart = await _cartRepository.GetByIdAsync(new CartId(query.CartId), cancellationToken);
        return cart is null
            ? Error.NotFound("cart.not_found", "Panier introuvable.")
            : await CartPricer.PriceAsync(cart, _pricing, _ordering, cancellationToken);
    }
}
