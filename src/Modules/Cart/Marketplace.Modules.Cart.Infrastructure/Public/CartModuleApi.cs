using MediatR;
using Marketplace.Modules.Cart.Application.Carts.Queries;
using Marketplace.Modules.Cart.Contracts;

namespace Marketplace.Modules.Cart.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Cart. Délègue aux
/// requêtes de l'Application (valorisation via Pricing) ; renvoie null si absent.
/// </summary>
internal sealed class CartModuleApi : ICartModuleApi
{
    private readonly ISender _sender;

    public CartModuleApi(ISender sender) => _sender = sender;

    public async Task<CartSummary?> GetActiveCartAsync(Guid buyerId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetActiveCartQuery(buyerId), cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<CartSummary?> GetCartAsync(Guid cartId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetCartByIdQuery(cartId), cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }
}
