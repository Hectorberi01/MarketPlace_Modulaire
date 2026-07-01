using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Cart.Contracts;
using Marketplace.Modules.Inventory.Contracts;
using Marketplace.Modules.Ordering.Application.Abstractions;
using Marketplace.Modules.Ordering.Domain.Orders;

namespace Marketplace.Modules.Ordering.Application.Orders.Commands.PlaceOrder;

/// <summary>
/// Orchestrateur du Saga de commande. Lit le panier valorisé (Cart), fige les
/// prix dans une commande, réserve le stock ligne par ligne (Inventory) et place
/// la commande en attente de paiement. En cas d'échec de réservation, il libère
/// (compensation) ce qui avait déjà été réservé et marque la commande en échec.
/// </summary>
internal sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, Guid>
{
    private readonly ICartModuleApi _cartModuleApi;
    private readonly IInventoryModuleApi _inventoryModuleApi;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderingUnitOfWork _unitOfWork;

    public PlaceOrderCommandHandler(
        ICartModuleApi cartModuleApi,
        IInventoryModuleApi inventoryModuleApi,
        IOrderRepository orderRepository,
        IOrderingUnitOfWork unitOfWork)
    {
        _cartModuleApi = cartModuleApi;
        _inventoryModuleApi = inventoryModuleApi;
        _orderRepository = orderRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        var cart = await _cartModuleApi.GetActiveCartAsync(command.BuyerId, cancellationToken);
        if (cart is null || cart.Lines.Count == 0)
        {
            return Result.Failure<Guid>(Error.Conflict("ordering.cart_empty", "Aucun panier valorisé à commander."));
        }

        var drafts = cart.Lines.Select(l => new OrderLineDraft(
            l.OfferId, l.ProductId, l.SellerId, l.Sku, l.ShipFromLocationId, l.Quantity,
            l.UnitBaseAmount, l.SellerDiscount, l.PlatformDiscount, l.FinalUnitPrice));

        var orderResult = Order.Create(command.BuyerId, cart.CartId, cart.Currency, drafts);
        if (orderResult.IsFailure)
        {
            return Result.Failure<Guid>(orderResult.Error);
        }

        var order = orderResult.Value;

        // Fige l'adresse de livraison choisie au checkout (snapshot).
        if (command.ShippingAddress is { } addr)
        {
            order.SetShippingAddress(addr.Label, addr.Recipient, addr.Line1, addr.Line2, addr.City, addr.Country, addr.Phone);
        }

        // Fige les frais de livraison (forfait choisi) : entrent dans le total et
        // alimenteront le solde « frais de livraison » de la plateforme.
        order.SetShippingFee(command.ShippingFee);

        await _orderRepository.AddAsync(order, cancellationToken);

        // --- Étape Saga : réservation du stock, avec compensation en cas d'échec.
        var reserved = new List<(string Sku, Guid LocationId)>();
        foreach (var line in order.Lines)
        {
            var ok = await _inventoryModuleApi.TryReserveAsync(
                line.Sku, line.ShipFromLocationId, order.Id.Value, line.Quantity, cancellationToken);

            if (ok)
            {
                reserved.Add((line.Sku, line.ShipFromLocationId));
                continue;
            }

            // Compensation : on libère les réservations déjà obtenues.
            foreach (var r in reserved)
            {
                await _inventoryModuleApi.ReleaseReservationAsync(r.Sku, r.LocationId, order.Id.Value, cancellationToken);
            }

            order.Fail($"Stock indisponible pour le SKU {line.Sku}.");
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<Guid>(Error.Conflict("ordering.out_of_stock", $"Stock insuffisant pour {line.Sku}."));
        }

        order.MarkAwaitingPayment();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return order.Id.Value;
    }
}
