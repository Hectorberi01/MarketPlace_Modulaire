using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Inventory.Contracts;
using Marketplace.Modules.Ordering.Application.Abstractions;
using Marketplace.Modules.Ordering.Domain.Orders;
using Marketplace.Modules.Settlement.Contracts;

namespace Marketplace.Modules.Ordering.Application.Orders.Commands;

/// <summary>Confirme le paiement d'une commande (webhook PSP / admin) : solde le stock et confirme.</summary>
public sealed record ConfirmOrderPaymentCommand(Guid OrderId) : ICommand;

/// <summary>Annule une commande et libère ses réservations (compensation).</summary>
public sealed record CancelOrderCommand(Guid OrderId, string Reason) : ICommand;

/// <summary>Marque une commande comme livrée (déclenche escrow + payout en aval).</summary>
public sealed record MarkOrderDeliveredCommand(Guid OrderId) : ICommand;

internal sealed class MarkOrderDeliveredCommandHandler : ICommandHandler<MarkOrderDeliveredCommand>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderingUnitOfWork _unitOfWork;

    public MarkOrderDeliveredCommandHandler(IOrderRepository orderRepository, IOrderingUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(MarkOrderDeliveredCommand command, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(new OrderId(command.OrderId), cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("ordering.not_found", "Commande introuvable."));
        }

        var delivered = order.MarkDelivered();
        if (delivered.IsFailure)
        {
            return delivered;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class ConfirmOrderPaymentCommandHandler : ICommandHandler<ConfirmOrderPaymentCommand>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IInventoryModuleApi _inventoryModuleApi;
    private readonly ISettlementModuleApi _settlementModuleApi;
    private readonly IOrderingUnitOfWork _unitOfWork;

    public ConfirmOrderPaymentCommandHandler(
        IOrderRepository orderRepository,
        IInventoryModuleApi inventoryModuleApi,
        ISettlementModuleApi settlementModuleApi,
        IOrderingUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _inventoryModuleApi = inventoryModuleApi;
        _settlementModuleApi = settlementModuleApi;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ConfirmOrderPaymentCommand command, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(new OrderId(command.OrderId), cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("ordering.not_found", "Commande introuvable."));
        }

        var paid = order.MarkPaid();
        if (paid.IsFailure)
        {
            return paid;
        }

        // La part réglée par la cagnotte est CONSOMMÉE : elle quitte le réservé pour de
        // bon. Avant le stock, parce que c'est le seul des deux qui peut encore refuser —
        // et qu'il vaut mieux le découvrir avant d'avoir décrémenté l'inventaire.
        if (order.WalletApplied > 0m)
        {
            await _settlementModuleApi.ConfirmCreditAsync(
                order.BuyerId, order.Id.Value, order.WalletApplied, order.Currency, cancellationToken);
        }

        // Solde des réservations -> décrément du stock physique.
        foreach (var line in order.Lines)
        {
            await _inventoryModuleApi.ConfirmReservationAsync(line.Sku, line.ShipFromLocationId, order.Id.Value, cancellationToken);
        }

        var confirmed = order.Confirm();
        if (confirmed.IsFailure)
        {
            return confirmed;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class CancelOrderCommandHandler : ICommandHandler<CancelOrderCommand>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IInventoryModuleApi _inventoryModuleApi;
    private readonly ISettlementModuleApi _settlementModuleApi;
    private readonly IOrderingUnitOfWork _unitOfWork;

    public CancelOrderCommandHandler(
        IOrderRepository orderRepository,
        IInventoryModuleApi inventoryModuleApi,
        ISettlementModuleApi settlementModuleApi,
        IOrderingUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _inventoryModuleApi = inventoryModuleApi;
        _settlementModuleApi = settlementModuleApi;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(new OrderId(command.OrderId), cancellationToken);
        if (order is null)
        {
            return Result.Failure(Error.NotFound("ordering.not_found", "Commande introuvable."));
        }

        var cancel = order.Cancel(string.IsNullOrWhiteSpace(command.Reason) ? "Annulée par l'utilisateur." : command.Reason);
        if (cancel.IsFailure)
        {
            return cancel;
        }

        // Compensation : libération des réservations de stock.
        foreach (var line in order.Lines)
        {
            await _inventoryModuleApi.ReleaseReservationAsync(line.Sku, line.ShipFromLocationId, order.Id.Value, cancellationToken);
        }

        // Et de la cagnotte engagée. Sans cette ligne, l'argent d'un client dont le
        // paiement a échoué resterait bloqué dans le réservé : ni dépensable, ni
        // retirable, ni visible — et aucun écran ne saurait lui dire où il est passé.
        if (order.WalletApplied > 0m)
        {
            await _settlementModuleApi.ReleaseCreditAsync(
                order.BuyerId, order.Id.Value, order.WalletApplied, order.Currency, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
