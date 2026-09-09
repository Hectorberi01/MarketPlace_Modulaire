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

        // ═════════════════════════════════════════════════════════════════════════
        // REJEU : LA COMMANDE EST DÉJÀ CONFIRMÉE, C'EST UN SUCCÈS.
        //
        // L'outbox livre AU MOINS UNE FOIS. Sans cette garde, un second passage
        // échouait sur `MarkPaid` — et cet échec devenant désormais une exception qui
        // fait rejouer puis mettre en lettre morte, un rejeu parfaitement normal
        // finirait en alerte. La garde doit précéder la remontée d'erreur, pas la suivre.
        // ═════════════════════════════════════════════════════════════════════════
        if (order.Status is OrderStatus.Confirmed or OrderStatus.Delivered)
        {
            return Result.Success();
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

        // ═════════════════════════════════════════════════════════════════════════
        // SOLDE DES RÉSERVATIONS. LE RÉSULTAT SE LIT, IL NE SE JETTE PLUS.
        //
        // `ConfirmReservationAsync` renvoie un booléen, et ce booléen est significatif :
        // il vaut faux quand l'article a disparu ou qu'aucune réservation ne subsiste
        // pour cette commande. Il était ignoré ici comme ailleurs — la commande passait
        // `Confirmed`, l'expédition était créée, le vendeur crédité, SANS QUE `OnHand`
        // AIT ÉTÉ DÉCRÉMENTÉ. La marchandise était vendue deux fois.
        //
        // Le défaut était connu : deux commentaires du dépôt le décrivaient comme un
        // fait acquis, et un test le documentait. Le code s'était organisé autour du
        // bug au lieu de le corriger.
        //
        // ON ÉCHOUE PLUTÔT QUE DE CONFIRMER. L'échec fait remonter le message à
        // l'outbox, qui rejouera puis mettra en lettre morte — un humain verra une
        // commande payée non confirmée, ce qui se répare. Une survente, non : elle se
        // découvre au moment de préparer le colis, quand l'argent est encaissé et le
        // stock introuvable.
        // ═════════════════════════════════════════════════════════════════════════
        foreach (var line in order.Lines)
        {
            var settled = await _inventoryModuleApi.ConfirmReservationAsync(
                line.Sku, line.ShipFromLocationId, order.Id.Value, cancellationToken);

            if (!settled)
            {
                return Result.Failure(Error.Conflict(
                    "ordering.reservation_not_settled",
                    $"Le stock de « {line.Sku} » n'a pas pu être soldé pour cette commande. "
                    + "La commande N'EST PAS confirmée : la confirmer sans décrémenter le stock "
                    + "reviendrait à vendre deux fois le même article."));
            }
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

        // Même raison que pour la confirmation : un rejeu d'outbox sur une commande
        // déjà annulée est un succès, pas une anomalie à faire remonter.
        if (order.Status == OrderStatus.Cancelled)
        {
            return Result.Success();
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
