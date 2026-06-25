using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Cart.Application.Abstractions;
using Marketplace.Modules.Cart.Domain.Carts;

namespace Marketplace.Modules.Cart.Application.Carts.Commands;

/// <summary>Change la quantité d'une ligne (0 ou moins la retire).</summary>
public sealed record UpdateCartItemQuantityCommand(Guid BuyerId, Guid OfferId, int Quantity) : ICommand;

/// <summary>Retire une ligne du panier.</summary>
public sealed record RemoveCartItemCommand(Guid BuyerId, Guid OfferId) : ICommand;

/// <summary>Vide le panier actif.</summary>
public sealed record ClearCartCommand(Guid BuyerId) : ICommand;

/// <summary>Valide le panier (checkout) — déclenche la création de commande côté Ordering.</summary>
public sealed record CheckoutCartCommand(Guid BuyerId) : ICommand<Guid>;

internal abstract class CartCommandHandlerBase
{
    protected readonly ICartRepository CartRepository;
    protected readonly ICartUnitOfWork UnitOfWork;
    private readonly ICacheService _cache;

    protected CartCommandHandlerBase(ICartRepository cartRepository, ICartUnitOfWork unitOfWork, ICacheService cache)
    {
        CartRepository = cartRepository;
        UnitOfWork = unitOfWork;
        _cache = cache;
    }

    /// <summary>Persiste puis invalide le cache du panier de l'acheteur (read-your-writes).</summary>
    protected async Task SaveAndInvalidateAsync(Guid buyerId, CancellationToken cancellationToken)
    {
        await UnitOfWork.SaveChangesAsync(cancellationToken);
        await _cache.RemoveAsync(CartCacheKeys.Active(buyerId), cancellationToken);
    }
}

internal sealed class UpdateCartItemQuantityCommandHandler
    : CartCommandHandlerBase, ICommandHandler<UpdateCartItemQuantityCommand>
{
    public UpdateCartItemQuantityCommandHandler(ICartRepository cartRepository, ICartUnitOfWork unitOfWork, ICacheService cache)
        : base(cartRepository, unitOfWork, cache) { }

    public async Task<Result> Handle(UpdateCartItemQuantityCommand command, CancellationToken cancellationToken)
    {
        var cart = await CartRepository.GetActiveByBuyerAsync(command.BuyerId, cancellationToken);
        if (cart is null)
        {
            return Result.Failure(Error.NotFound("cart.not_found", "Aucun panier actif."));
        }

        var result = cart.UpdateItemQuantity(command.OfferId, command.Quantity);
        if (result.IsFailure)
        {
            return result;
        }

        await SaveAndInvalidateAsync(command.BuyerId, cancellationToken);
        return Result.Success();
    }
}

internal sealed class RemoveCartItemCommandHandler
    : CartCommandHandlerBase, ICommandHandler<RemoveCartItemCommand>
{
    public RemoveCartItemCommandHandler(ICartRepository cartRepository, ICartUnitOfWork unitOfWork, ICacheService cache)
        : base(cartRepository, unitOfWork, cache) { }

    public async Task<Result> Handle(RemoveCartItemCommand command, CancellationToken cancellationToken)
    {
        var cart = await CartRepository.GetActiveByBuyerAsync(command.BuyerId, cancellationToken);
        if (cart is null)
        {
            return Result.Failure(Error.NotFound("cart.not_found", "Aucun panier actif."));
        }

        var result = cart.RemoveItem(command.OfferId);
        if (result.IsFailure)
        {
            return result;
        }

        await SaveAndInvalidateAsync(command.BuyerId, cancellationToken);
        return Result.Success();
    }
}

internal sealed class ClearCartCommandHandler
    : CartCommandHandlerBase, ICommandHandler<ClearCartCommand>
{
    public ClearCartCommandHandler(ICartRepository cartRepository, ICartUnitOfWork unitOfWork, ICacheService cache)
        : base(cartRepository, unitOfWork, cache) { }

    public async Task<Result> Handle(ClearCartCommand command, CancellationToken cancellationToken)
    {
        var cart = await CartRepository.GetActiveByBuyerAsync(command.BuyerId, cancellationToken);
        if (cart is null)
        {
            return Result.Failure(Error.NotFound("cart.not_found", "Aucun panier actif."));
        }

        cart.Clear();
        await SaveAndInvalidateAsync(command.BuyerId, cancellationToken);
        return Result.Success();
    }
}

internal sealed class CheckoutCartCommandHandler
    : CartCommandHandlerBase, ICommandHandler<CheckoutCartCommand, Guid>
{
    public CheckoutCartCommandHandler(ICartRepository cartRepository, ICartUnitOfWork unitOfWork, ICacheService cache)
        : base(cartRepository, unitOfWork, cache) { }

    public async Task<Result<Guid>> Handle(CheckoutCartCommand command, CancellationToken cancellationToken)
    {
        var cart = await CartRepository.GetActiveByBuyerAsync(command.BuyerId, cancellationToken);
        if (cart is null)
        {
            return Result.Failure<Guid>(Error.NotFound("cart.not_found", "Aucun panier actif."));
        }

        var result = cart.MarkCheckedOut();
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await SaveAndInvalidateAsync(command.BuyerId, cancellationToken);
        return cart.Id.Value;
    }
}
