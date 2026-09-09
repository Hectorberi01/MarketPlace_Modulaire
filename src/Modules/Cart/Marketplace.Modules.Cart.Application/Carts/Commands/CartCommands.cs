using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Cart.Application.Abstractions;
using Marketplace.Modules.Cart.Domain.Carts;
using Marketplace.Modules.Pricing.Contracts;

namespace Marketplace.Modules.Cart.Application.Carts.Commands;

/// <summary>Change la quantité d'une ligne (0 ou moins la retire).</summary>
public sealed record UpdateCartItemQuantityCommand(Guid BuyerId, Guid OfferId, int Quantity) : ICommand;

/// <summary>Retire une ligne du panier.</summary>
public sealed record RemoveCartItemCommand(Guid BuyerId, Guid OfferId) : ICommand;

/// <summary>Vide le panier actif.</summary>
public sealed record ClearCartCommand(Guid BuyerId) : ICommand;

// ═════════════════════════════════════════════════════════════════════════════
// `CheckoutCartCommand` A ÉTÉ SUPPRIMÉE, AINSI QUE SON HANDLER.
//
// Sa documentation affirmait qu'elle « déclenche la création de commande côté
// Ordering ». C'ÉTAIT FAUX, et de la pire façon : le handler faisait
// `Cart.MarkCheckedOut()` puis sauvegardait, rien d'autre. Aucune commande n'était
// créée, et l'`CartCheckedOutIntegrationEvent` publié dans la foulée n'avait aucun
// consommateur dans le dépôt. Le panier passait `CheckedOut`, la lecture du panier
// actif filtre sur `Active`, et l'agrégat n'a aucune méthode de réouverture :
// l'acheteur perdait son panier définitivement, en échange de rien.
//
// Son unique appelant était `POST /api/cart/checkout`, supprimé en même temps
// (voir l'encadré dans `CartEndpoints.MapCartEndpoints`). Aucun autre appelant dans
// `src` ni dans `tests` — la commande n'avait donc plus de raison d'exister, et la
// laisser en place aurait invité à la rebrancher.
//
// La fermeture du panier n'a plus qu'un seul chemin, et c'est une RÉACTION :
// `CloseCartOnOrderPlacedHandler`, sur `OrderPlaced`. Le panier ne se ferme donc
// jamais sans qu'une commande existe pour le remplacer.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Applique un code promo au panier.</summary>
public sealed record ApplyCouponCommand(Guid BuyerId, string Code) : ICommand;

/// <summary>Retire le code promo du panier.</summary>
public sealed record RemoveCouponCommand(Guid BuyerId) : ICommand;

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

/// <summary>
/// Attache un code promo au panier — après l'avoir fait valider par Pricing.
///
/// La validation est INDICATIVE : elle sert à répondre tout de suite « code expiré » ou
/// « déjà utilisé », plutôt que de laisser l'acheteur porter un code mort jusqu'au
/// checkout et découvrir que la remise ne s'applique pas. Elle n'engage rien : le coupon
/// n'est réellement consommé qu'à la CONFIRMATION de la commande, sous verrou
/// (RedeemPromotionOnOrderConfirmedHandler).
///
/// Le panier, lui, ne sait pas ce qu'est une promotion — et ne doit pas le savoir. Il
/// stocke une chaîne ; Pricing en fait le sens.
/// </summary>
internal sealed class ApplyCouponCommandHandler
    : CartCommandHandlerBase, ICommandHandler<ApplyCouponCommand>
{
    private readonly IPricingModuleApi _pricing;

    public ApplyCouponCommandHandler(
        ICartRepository cartRepository, ICartUnitOfWork unitOfWork, ICacheService cache, IPricingModuleApi pricing)
        : base(cartRepository, unitOfWork, cache) => _pricing = pricing;

    public async Task<Result> Handle(ApplyCouponCommand command, CancellationToken cancellationToken)
    {
        var cart = await CartRepository.GetActiveByBuyerAsync(command.BuyerId, cancellationToken);
        if (cart is null)
        {
            return Result.Failure(Error.NotFound("cart.not_found", "Aucun panier actif."));
        }

        var validation = await _pricing.ValidateCouponAsync(command.Code, command.BuyerId, cancellationToken);
        if (!validation.IsValid)
        {
            return Result.Failure(Error.Validation(
                validation.ErrorCode ?? "pricing.coupon.invalid",
                validation.ErrorMessage ?? "Ce code promo est invalide."));
        }

        var result = cart.ApplyPromotionCode(command.Code);
        if (result.IsFailure)
        {
            return result;
        }

        // Invalide le panier valorisé en cache : sans cela, l'acheteur appliquerait son
        // code et verrait pendant 2 minutes l'ANCIEN total, sans remise. Il croirait le
        // code refusé et le ressaisirait — ou abandonnerait.
        await SaveAndInvalidateAsync(command.BuyerId, cancellationToken);
        return Result.Success();
    }
}

internal sealed class RemoveCouponCommandHandler
    : CartCommandHandlerBase, ICommandHandler<RemoveCouponCommand>
{
    public RemoveCouponCommandHandler(ICartRepository cartRepository, ICartUnitOfWork unitOfWork, ICacheService cache)
        : base(cartRepository, unitOfWork, cache) { }

    public async Task<Result> Handle(RemoveCouponCommand command, CancellationToken cancellationToken)
    {
        var cart = await CartRepository.GetActiveByBuyerAsync(command.BuyerId, cancellationToken);
        if (cart is null)
        {
            return Result.Failure(Error.NotFound("cart.not_found", "Aucun panier actif."));
        }

        var result = cart.RemovePromotionCode();
        if (result.IsFailure)
        {
            return result;
        }

        await SaveAndInvalidateAsync(command.BuyerId, cancellationToken);
        return Result.Success();
    }
}
