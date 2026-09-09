using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Inventory.Application.Abstractions;
using Marketplace.Modules.Inventory.Domain.Common;
using Marketplace.Modules.Inventory.Domain.Stock;

namespace Marketplace.Modules.Inventory.Application.Stock.Commands;

// ---- Réservation ----------------------------------------------------------

/// <summary>Réserve du stock d'un SKU sur une localisation pour une commande.</summary>
/// <param name="ExpiresInMinutes">
/// Durée d'immobilisation du stock. SIX HEURES, ALIGNÉES SUR LE BALAYEUR DES COMMANDES
/// ABANDONNÉES — et non plus quinze minutes.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LES DEUX NOMBRES DOIVENT S'ACCORDER, ET C'EST LE PLUS LONG QUI COMMANDE.
///
/// La valeur était de quinze minutes, mais `ExpiresAtUtc` n'était lue par personne : en
/// pratique le stock restait immobilisé jusqu'à ce que `AbandonedOrderSweeper` annule la
/// commande, six heures plus tard. Rendre l'expiration effective SANS toucher à la durée
/// aurait donc libéré le stock au bout de quinze minutes — alors que la plateforme
/// accepte encore un paiement Mobile Money pendant six heures. Le premier arrivant aurait
/// pris l'article, et la confirmation du payeur lent aurait fait passer le stock physique
/// sous zéro : une survente, créée par une correction.
///
/// La fenêtre de réservation ne peut pas être plus courte que la fenêtre de paiement. Si
/// tu veux libérer le stock plus vite, c'est le SEUIL DU BALAYEUR qu'il faut baisser
/// d'abord, et celui-ci ensuite — jamais l'inverse.
///
/// CE QUE CETTE CORRECTION APPORTE MALGRÉ TOUT : l'expiration devient un second filet,
/// indépendant du balayeur. Avant, une commande que le balayeur ne traitait pas — hôte
/// arrêté, lot en échec — immobilisait son stock INDÉFINIMENT.
/// ═════════════════════════════════════════════════════════════════════════════
/// </param>
public sealed record ReserveStockCommand(
    string Sku, Guid LocationId, Guid OrderId, int Quantity, int ExpiresInMinutes = 360) : ICommand;

public sealed class ReserveStockCommandValidator : AbstractValidator<ReserveStockCommand>
{
    public ReserveStockCommandValidator()
    {
        RuleFor(c => c.Sku).NotEmpty();
        RuleFor(c => c.LocationId).NotEmpty();
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Quantity).GreaterThan(0);
        RuleFor(c => c.ExpiresInMinutes).GreaterThan(0);
    }
}

internal sealed class ReserveStockCommandHandler : ICommandHandler<ReserveStockCommand>
{
    private readonly IInventoryItemRepository _repository;
    private readonly IInventoryUnitOfWork _unitOfWork;

    public ReserveStockCommandHandler(IInventoryItemRepository repository, IInventoryUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ReserveStockCommand command, CancellationToken cancellationToken)
    {
        var item = await ResolveItem(_repository, command.Sku, command.LocationId, cancellationToken);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("inventory.item.not_found", "Article de stock introuvable."));
        }

        var result = item.Reserve(command.OrderId, command.Quantity, DateTime.UtcNow.AddMinutes(command.ExpiresInMinutes));
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    internal static async Task<InventoryItem?> ResolveItem(
        IInventoryItemRepository repository, string sku, Guid locationId, CancellationToken ct)
    {
        var skuResult = Sku.Create(sku);
        return skuResult.IsFailure ? null : await repository.GetBySkuAndLocationAsync(skuResult.Value.Value, locationId, ct);
    }
}

// ---- Libération -----------------------------------------------------------

/// <summary>Libère la réservation d'une commande (paiement échoué / annulation).</summary>
public sealed record ReleaseReservationCommand(string Sku, Guid LocationId, Guid OrderId) : ICommand;

internal sealed class ReleaseReservationCommandHandler : ICommandHandler<ReleaseReservationCommand>
{
    private readonly IInventoryItemRepository _repository;
    private readonly IInventoryUnitOfWork _unitOfWork;

    public ReleaseReservationCommandHandler(IInventoryItemRepository repository, IInventoryUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ReleaseReservationCommand command, CancellationToken cancellationToken)
    {
        var item = await ReserveStockCommandHandler.ResolveItem(_repository, command.Sku, command.LocationId, cancellationToken);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("inventory.item.not_found", "Article de stock introuvable."));
        }

        item.ReleaseReservation(command.OrderId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

// ---- Confirmation (vente) -------------------------------------------------

/// <summary>Confirme la vente : décrémente le stock physique et solde la réservation.</summary>
public sealed record ConfirmReservationCommand(string Sku, Guid LocationId, Guid OrderId) : ICommand;

internal sealed class ConfirmReservationCommandHandler : ICommandHandler<ConfirmReservationCommand>
{
    private readonly IInventoryItemRepository _repository;
    private readonly IInventoryUnitOfWork _unitOfWork;

    public ConfirmReservationCommandHandler(IInventoryItemRepository repository, IInventoryUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ConfirmReservationCommand command, CancellationToken cancellationToken)
    {
        var item = await ReserveStockCommandHandler.ResolveItem(_repository, command.Sku, command.LocationId, cancellationToken);
        if (item is null)
        {
            return Result.Failure(Error.NotFound("inventory.item.not_found", "Article de stock introuvable."));
        }

        var result = item.ConfirmReservation(command.OrderId);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
