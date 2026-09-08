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
            l.UnitBaseAmount, l.SellerDiscount, l.PlatformDiscount, l.FinalUnitPrice,
            l.CommissionRate, l.ProviderFeeRate));

        // Le code promo du panier est FIGÉ dans la commande. C'est indispensable : le
        // panier est clôturé juste après, et le coupon n'est décompté qu'à la
        // CONFIRMATION (paiement encaissé). Sans ce report, Pricing recevrait un
        // « commande confirmée » sans savoir quel coupon consommer — et le coupon
        // resterait éternellement disponible. C'est exactement le bug d'origine.
        var orderResult = Order.Create(command.BuyerId, cart.CartId, cart.Currency, drafts, cart.PromotionCode);
        if (orderResult.IsFailure)
        {
            return Result.Failure<Guid>(orderResult.Error);
        }

        var order = orderResult.Value;

        // ─────────────────────────────────────────────────────────────────────────────
        // L'ADRESSE DE LIVRAISON EST UN INVARIANT DE LA COMMANDE, PAS UNE VÉRIFICATION
        // D'INTERFACE.
        //
        // Ce contrôle vivait dans un `if` du BFF Mobile, et il n'y suffisait pas :
        // `PayRequest.AddressId` vaut `null` par défaut, si bien qu'omettre simplement le
        // champ passait à côté du garde-fou et créait une commande SANS adresse — payée.
        // Deux autres surfaces (l'API historique et le BFF Web) appellent d'ailleurs cette
        // commande sans jamais fournir d'adresse.
        //
        // Un invariant qui n'est vrai que sur l'un des trois chemins d'écriture n'est pas
        // un invariant. Il est donc ici, là où toutes les commandes passent.
        //
        // Ce qu'on exige : la COMMUNE et le POINT DE REPÈRE. Pas la rue — au Bénin elle
        // est souvent inexistante, et l'exiger reviendrait à faire inventer une adresse.
        // Le repère, lui, est ce que le coursier utilise vraiment.
        // ─────────────────────────────────────────────────────────────────────────────
        if (command.ShippingAddress is not { } addr
            || string.IsNullOrWhiteSpace(addr.CommuneCode)
            || string.IsNullOrWhiteSpace(addr.Landmark))
        {
            return Result.Failure<Guid>(Error.Validation(
                "ordering.shipping_address_required",
                "Une adresse de livraison complète est obligatoire : commune et point de repère."));
        }

        order.SetShippingAddress(
            addr.Label, addr.Recipient, addr.Phone,
            addr.CommuneCode, addr.Quartier, addr.Landmark, addr.Line1, addr.CountryCode,
            addr.Latitude, addr.Longitude);

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
