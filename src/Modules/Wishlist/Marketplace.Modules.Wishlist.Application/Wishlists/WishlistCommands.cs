using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Wishlist.Application.Abstractions;
using WishlistAggregate = Marketplace.Modules.Wishlist.Domain.Wishlists.Wishlist;
using Marketplace.Modules.Wishlist.Domain.Wishlists;

namespace Marketplace.Modules.Wishlist.Application.Wishlists;

/// <summary>Ajoute (ou met à jour) un produit dans la liste d'envies de l'utilisateur.</summary>
public sealed record AddToWishlistCommand(Guid UserId, Guid ProductId, Guid? OfferId, bool PriceAlert, bool StockAlert) : ICommand;

/// <summary>Retire un produit de la liste d'envies.</summary>
public sealed record RemoveFromWishlistCommand(Guid UserId, Guid ProductId) : ICommand;

/// <summary>Active/désactive les alertes prix/stock d'un produit suivi.</summary>
public sealed record SetWishlistAlertsCommand(Guid UserId, Guid ProductId, bool PriceAlert, bool StockAlert) : ICommand;

internal sealed class AddToWishlistCommandHandler : ICommandHandler<AddToWishlistCommand>
{
    private readonly IWishlistRepository _repository;
    private readonly IWishlistUnitOfWork _unitOfWork;

    public AddToWishlistCommandHandler(IWishlistRepository repository, IWishlistUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AddToWishlistCommand command, CancellationToken cancellationToken)
    {
        var wishlist = await _repository.GetByUserAsync(command.UserId, cancellationToken);
        if (wishlist is null)
        {
            var created = WishlistAggregate.Create(command.UserId);
            if (created.IsFailure)
            {
                return created;
            }

            wishlist = created.Value;
            await _repository.AddAsync(wishlist, cancellationToken);
        }

        var result = wishlist.AddItem(command.ProductId, command.OfferId, command.PriceAlert, command.StockAlert);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class RemoveFromWishlistCommandHandler : ICommandHandler<RemoveFromWishlistCommand>
{
    private readonly IWishlistRepository _repository;
    private readonly IWishlistUnitOfWork _unitOfWork;

    public RemoveFromWishlistCommandHandler(IWishlistRepository repository, IWishlistUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RemoveFromWishlistCommand command, CancellationToken cancellationToken)
    {
        var wishlist = await _repository.GetByUserAsync(command.UserId, cancellationToken);
        if (wishlist is null)
        {
            return Result.Failure(Error.NotFound("wishlist.not_found", "Aucune liste d'envies."));
        }

        var result = wishlist.RemoveItem(command.ProductId);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class SetWishlistAlertsCommandHandler : ICommandHandler<SetWishlistAlertsCommand>
{
    private readonly IWishlistRepository _repository;
    private readonly IWishlistUnitOfWork _unitOfWork;

    public SetWishlistAlertsCommandHandler(IWishlistRepository repository, IWishlistUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SetWishlistAlertsCommand command, CancellationToken cancellationToken)
    {
        var wishlist = await _repository.GetByUserAsync(command.UserId, cancellationToken);
        if (wishlist is null)
        {
            return Result.Failure(Error.NotFound("wishlist.not_found", "Aucune liste d'envies."));
        }

        var result = wishlist.SetAlerts(command.ProductId, command.PriceAlert, command.StockAlert);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
