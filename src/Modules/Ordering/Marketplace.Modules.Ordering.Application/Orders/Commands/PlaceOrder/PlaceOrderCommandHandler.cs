using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Geography;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Cart.Contracts;
using Marketplace.Modules.Inventory.Contracts;
using Marketplace.Modules.Ordering.Application.Abstractions;
using Marketplace.Modules.Ordering.Domain.Orders;
using Marketplace.Modules.Settlement.Contracts;
using Marketplace.Modules.Tax.Contracts;

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
    private readonly ISettlementModuleApi _settlementModuleApi;
    private readonly ITaxModuleApi _taxModuleApi;
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderingUnitOfWork _unitOfWork;

    public PlaceOrderCommandHandler(
        ICartModuleApi cartModuleApi,
        IInventoryModuleApi inventoryModuleApi,
        ISettlementModuleApi settlementModuleApi,
        ITaxModuleApi taxModuleApi,
        IOrderRepository orderRepository,
        IOrderingUnitOfWork unitOfWork)
    {
        _cartModuleApi = cartModuleApi;
        _inventoryModuleApi = inventoryModuleApi;
        _settlementModuleApi = settlementModuleApi;
        _taxModuleApi = taxModuleApi;
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

        // ═════════════════════════════════════════════════════════════════════════
        // LE PRIX ANNONCÉ EST-IL TOUJOURS LE PRIX DÛ ? ICI, ET NULLE PART AILLEURS.
        //
        // Le contrôle est posé APRÈS `SetShippingFee` — qui déclenche le recalcul des
        // totaux — et AVANT `AddAsync` : c'est le seul point du parcours où le total
        // définitif existe et où rien n'a encore été écrit. Le placer plus tôt
        // comparerait un total incomplet ; plus tard obligerait à défaire une commande
        // enregistrée, avec ses réservations de stock et sa cagnotte engagée.
        //
        // Le refuser dans le BFF, avant d'envoyer la commande, aurait laissé une fenêtre :
        // le panier y est valorisé une fois pour le contrôle, puis une SECONDE fois ici.
        // Deux valorisations, deux résultats possibles — c'est exactement le défaut qu'on
        // corrige, reproduit en plus petit. Un seul calcul doit servir de référence, et
        // c'est celui qui devient la commande.
        //
        // ÉGALITÉ STRICTE. Les deux montants viennent de `Money.Create`, arrondi une fois
        // à deux décimales, puis d'additions de valeurs déjà arrondies : une tolérance
        // n'aurait rien à absorber, et masquerait un vrai écart de centimes le jour où
        // l'un des deux chemins change.
        //
        // CONFLIT, PAS VALIDATION : la requête est bien formée, c'est le monde qui a
        // bougé entre l'écran et le bouton. Le montant réel est dans le message pour que
        // l'application le réaffiche au lieu de renvoyer l'acheteur au panier.
        // ═════════════════════════════════════════════════════════════════════════
        if (command.ExpectedTotal is { } expected && order.GrandTotal != expected)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "ordering.total_changed",
                $"Le montant a changé depuis l'affichage : {order.GrandTotal:0.##} {order.Currency} "
                + $"au lieu de {expected:0.##} {order.Currency}. Vérifiez votre panier avant de payer."));
        }

        // ═════════════════════════════════════════════════════════════════════════
        // VENTILATION DE LA TAXE — APRÈS le contrôle de total, et pour cause.
        //
        // Le régime est « TVA comprise » : cet appel ne change AUCUN montant. Il note
        // quelle part du total déjà arrêté est de la taxe, pour la comptabilité et la
        // facture. Le placer après le contrôle garantit qu'un refus n'aura interrogé
        // le module Tax pour rien, et surtout que la ventilation ne peut jamais
        // devenir une raison de faire varier `GrandTotal` : à cet endroit du code, le
        // total est déjà validé contre celui que l'acheteur a vu.
        //
        // CATÉGORIE ABSENTE, ET C'EST ASSUMÉ. `OrderLine` ne porte pas la catégorie du
        // produit : la résoudre exigerait un aller-retour au catalogue par ligne, au
        // milieu du chemin critique de la commande. On passe donc `Guid.Empty`, ce qui
        // ne fait correspondre que les règles de JURIDICTION — le comportement voulu
        // tant que la place applique un taux unique. Le jour où une catégorie devra
        // être taxée à part, c'est `OrderLine` qui devra porter sa catégorie, figée
        // comme le reste ; la ventilation deviendra alors ligne à ligne.
        //
        // UNE RÈGLE FISCALE MANQUANTE N'EST PAS UNE ERREUR. Le module renvoie une taxe
        // nulle, et la commande passe. Une place qui démarre sans barème enregistré
        // doit vendre ; c'est la déclaration qui attendra, pas l'acheteur.
        // ═════════════════════════════════════════════════════════════════════════
        var tax = await _taxModuleApi.ExtractIncludedTaxAsync(
            order.ShipToCountryCode ?? BeninGeography.CountryCode,
            Guid.Empty,
            order.GrandTotal,
            cancellationToken);

        order.ApplyIncludedTax(tax.TaxAmount, tax.Rate, tax.AppliedRuleId);

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

        // ─────────────────────────────────────────────────────────────────────────────
        // Étape Saga : engagement de la CAGNOTTE, après le stock et avant le paiement.
        //
        // Cet ordre n'est pas indifférent. Engager la cagnotte avant le stock obligerait
        // à la rendre dès qu'un article manque — une écriture de plus, pour rien, sur le
        // cas d'échec le plus fréquent du checkout. Le stock tranche en premier ; l'argent
        // ne bouge que pour une commande qui a une chance d'exister.
        //
        // Le montant réservé peut être INFÉRIEUR au montant demandé : entre l'affichage du
        // panier et la validation, la même cagnotte a pu être engagée depuis un autre
        // appareil. C'est ce qui est réellement réservé qui est imputé à la commande, et
        // le client paiera la différence au prestataire.
        // ─────────────────────────────────────────────────────────────────────────────
        if (command.WalletAmount > 0m)
        {
            var reservedCredit = await _settlementModuleApi.TryReserveCreditAsync(
                command.BuyerId, order.Id.Value, Math.Min(command.WalletAmount, order.GrandTotal),
                order.Currency, cancellationToken);

            order.ApplyWalletCredit(reservedCredit);
        }

        order.MarkAwaitingPayment();

        // ─────────────────────────────────────────────────────────────────────────────
        // LA CAGNOTTE COUVRE TOUT : IL N'Y A RIEN À ENCAISSER, DONC PAS DE PAIEMENT.
        //
        // C'est le seul chemin par lequel une commande se confirme sans qu'un prestataire
        // n'ait jamais été appelé. Il est ici, dans le Saga, et non derrière une commande
        // publique : une commande « confirmer sans payer » exposée au BFF serait une porte
        // ouverte pour confirmer n'importe quelle commande sans la régler.
        //
        // Le garde-fou est celui du domaine — IsFullyCoveredByWallet ne peut être vrai que
        // si la cagnotte a réellement été engagée à hauteur du total, ce qui vient d'une
        // écriture au grand livre et pas d'un paramètre d'appel.
        // ─────────────────────────────────────────────────────────────────────────────
        if (order.IsFullyCoveredByWallet)
        {
            var settled = await SettleWithWalletOnlyAsync(order, cancellationToken);
            if (settled.IsFailure)
            {
                return Result.Failure<Guid>(settled.Error);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return order.Id.Value;
    }

    /// <summary>
    /// Solde une commande intégralement réglée par la cagnotte : consommation de la
    /// réservation, décrément du stock, puis paiement et confirmation.
    ///
    /// Reprend pas à pas ce que fait <c>ConfirmOrderPaymentCommandHandler</c> à la
    /// réception d'un paiement encaissé. La duplication est assumée : factoriser
    /// obligerait à extraire un service partagé qui appelle la persistance de deux
    /// modules, et rendrait moins lisible le seul endroit où une commande se confirme
    /// sans argent extérieur.
    /// </summary>
    private async Task<Result> SettleWithWalletOnlyAsync(Order order, CancellationToken cancellationToken)
    {
        await _settlementModuleApi.ConfirmCreditAsync(
            order.BuyerId, order.Id.Value, order.WalletApplied, order.Currency, cancellationToken);

        foreach (var line in order.Lines)
        {
            await _inventoryModuleApi.ConfirmReservationAsync(
                line.Sku, line.ShipFromLocationId, order.Id.Value, cancellationToken);
        }

        var paid = order.MarkPaid();
        if (paid.IsFailure)
        {
            return paid;
        }

        return order.Confirm();
    }
}
