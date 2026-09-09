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
            l.UnitBaseAmount, l.SellerDiscount, l.PlatformDiscount, l.LineTotal,
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

        // ═════════════════════════════════════════════════════════════════════════
        // TOUT CE QUI SUIT EST COMPENSÉ, Y COMPRIS SUR EXCEPTION.
        //
        // Il n'existe aucune transaction englobante dans ce dépôt : chaque étape du
        // saga commite pour son propre compte, et la commande n'est écrite qu'à la
        // fin. La compensation était donc la SEULE garantie — et elle n'était
        // déclenchée que par un échec MÉTIER (`ok == false`).
        //
        // Or l'échec le plus attendu en production n'en est pas un : `TryReserveAsync`
        // LÈVE sur conflit de verrou optimiste, cas documenté ailleurs dans ce dépôt
        // et traduit en 409 tout en haut de la pile. Deux acheteurs sur le dernier
        // article, une coupure réseau, une panne de base : l'exception traversait le
        // handler, et les lignes déjà réservées n'étaient JAMAIS libérées. La commande
        // n'étant pas persistée, ces réservations restaient rattachées à un
        // identifiant qui n'existait dans aucune table — invisibles du balayeur
        // d'abandon, qui part de la table des commandes. Seule l'expiration à six
        // heures les rendait inoffensives, et le message du 409 — « Rien n'a été
        // enregistré » — était faux.
        //
        // Le `try` couvre donc aussi l'engagement de cagnotte, qui n'avait AUCUNE
        // compensation dans cet orchestrateur : `ReleaseCreditAsync` n'y était pas
        // appelé une seule fois, et son seul appelant du dépôt exige une commande
        // chargée depuis la base. Tout échec après l'engagement laissait donc l'argent
        // du client bloqué dans le réservé, définitivement : ni dépensable, ni
        // retirable, et sans aucun écran pour l'expliquer. Le stock avait un second
        // filet — son expiration ; la cagnotte n'en avait pas.
        // ═════════════════════════════════════════════════════════════════════════
        var reserved = new List<(string Sku, Guid LocationId)>();
        var walletReserved = 0m;

        try
        {
            foreach (var line in order.Lines)
            {
                var ok = await _inventoryModuleApi.TryReserveAsync(
                    line.Sku, line.ShipFromLocationId, order.Id.Value, line.Quantity, cancellationToken);

                if (ok)
                {
                    reserved.Add((line.Sku, line.ShipFromLocationId));
                    continue;
                }

                await CompensateAsync(order, reserved, walletReserved, cancellationToken);

                order.Fail($"Stock indisponible pour le SKU {line.Sku}.");
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return Result.Failure<Guid>(Error.Conflict("ordering.out_of_stock", $"Stock insuffisant pour {line.Sku}."));
            }

            // ─────────────────────────────────────────────────────────────────────
            // Étape Saga : engagement de la CAGNOTTE, après le stock et avant le paiement.
            //
            // Cet ordre n'est pas indifférent. Engager la cagnotte avant le stock
            // obligerait à la rendre dès qu'un article manque — une écriture de plus,
            // pour rien, sur le cas d'échec le plus fréquent du checkout. Le stock
            // tranche en premier ; l'argent ne bouge que pour une commande qui a une
            // chance d'exister.
            //
            // Le montant réservé peut être INFÉRIEUR au montant demandé : entre
            // l'affichage du panier et la validation, la même cagnotte a pu être
            // engagée depuis un autre appareil. C'est ce qui est réellement réservé
            // qui est imputé à la commande, et le client paiera la différence au
            // prestataire.
            // ─────────────────────────────────────────────────────────────────────
            if (command.WalletAmount > 0m)
            {
                walletReserved = await _settlementModuleApi.TryReserveCreditAsync(
                    command.BuyerId, order.Id.Value, Math.Min(command.WalletAmount, order.GrandTotal),
                    order.Currency, cancellationToken);

                order.ApplyWalletCredit(walletReserved);
            }

            order.MarkAwaitingPayment();

            // ═════════════════════════════════════════════════════════════════════
            // LA COMMANDE EST ÉCRITE ICI, AVANT TOUTE CONSOMMATION DÉFINITIVE.
            //
            // Ce `SaveChanges` n'existait pas : la commande n'était persistée qu'à la
            // toute fin, APRÈS que la cagnotte ait été débitée et le stock décrémenté
            // par le chemin « réglée intégralement en cagnotte ». Or cette sauvegarde
            // finale peut échouer — le contexte dispatche les événements de domaine
            // AVANT d'écrire, donc un handler qui lève emporte tout.
            //
            // Le résultat était le pire état atteignable du parcours : le client avait
            // payé, le stock était parti, et AUCUNE LIGNE DE COMMANDE N'EXISTAIT. Rien
            // n'était rattrapable — ni le balayeur d'abandon, ni les plafonds de
            // remboursement, qui ont tous besoin d'un identifiant de commande.
            //
            // En écrivant d'abord, le pire devient une commande « en attente de
            // paiement » avec sa cagnotte engagée : exactement ce que le balayeur sait
            // annuler et compenser six heures plus tard. Une panne coûte alors un
            // délai, plus une commande fantôme.
            // ═════════════════════════════════════════════════════════════════════
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // ─────────────────────────────────────────────────────────────────────
            // LA CAGNOTTE COUVRE TOUT : IL N'Y A RIEN À ENCAISSER, DONC PAS DE PAIEMENT.
            //
            // C'est le seul chemin par lequel une commande se confirme sans qu'un
            // prestataire n'ait jamais été appelé. Il est ici, dans le Saga, et non
            // derrière une commande publique : une commande « confirmer sans payer »
            // exposée au BFF serait une porte ouverte pour confirmer n'importe quelle
            // commande sans la régler.
            //
            // Le garde-fou est celui du domaine — `IsFullyCoveredByWallet` ne peut
            // être vrai que si la cagnotte a réellement été engagée à hauteur du
            // total, ce qui vient d'une écriture au grand livre et pas d'un paramètre
            // d'appel.
            // ─────────────────────────────────────────────────────────────────────
            if (order.IsFullyCoveredByWallet)
            {
                var settled = await SettleWithWalletOnlyAsync(order, cancellationToken);
                if (settled.IsFailure)
                {
                    // La commande EXISTE déjà en base, en attente de paiement, avec sa
                    // cagnotte engagée. On ne compense pas ici : le balayeur d'abandon
                    // l'annulera et rendra tout, par le chemin unique qui sait le
                    // faire. Compenser à la main doublerait ce chemin, et deux
                    // libérations de la même réservation finiraient par diverger.
                    return Result.Failure<Guid>(settled.Error);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception)
        {
            // On rend ce qui a été pris, PUIS on relaisse remonter. Avaler l'exception
            // transformerait une panne en commande silencieusement absente ; la
            // compenser sans la relancer ferait croire au client que rien n'a échoué.
            await CompensateAsync(order, reserved, walletReserved, CancellationToken.None);
            throw;
        }

        return order.Id.Value;
    }

    /// <summary>
    /// Rend ce que le saga a pris : réservations de stock et cagnotte engagée.
    ///
    /// LES ÉCHECS DE COMPENSATION SONT AVALÉS, ET C'EST DÉLIBÉRÉ. Cette méthode est
    /// appelée depuis un chemin d'erreur : lever ici masquerait la faute d'origine par
    /// une seconde, et laisserait les libérations SUIVANTES non tentées. Chaque ligne
    /// est donc compensée indépendamment, et ce qui échoue retombe sur les filets de
    /// second rang — l'expiration des réservations, le balayeur d'abandon.
    ///
    /// `CancellationToken.None` chez l'appelant du bloc `catch` : une requête annulée
    /// ne doit pas empêcher de rendre ce qu'elle avait pris.
    /// </summary>
    private async Task CompensateAsync(
        Order order,
        IReadOnlyList<(string Sku, Guid LocationId)> reserved,
        decimal walletReserved,
        CancellationToken cancellationToken)
    {
        foreach (var r in reserved)
        {
            try
            {
                await _inventoryModuleApi.ReleaseReservationAsync(
                    r.Sku, r.LocationId, order.Id.Value, cancellationToken);
            }
            catch (Exception)
            {
                // Rattrapé par l'expiration de la réservation.
            }
        }

        if (walletReserved <= 0m)
        {
            return;
        }

        try
        {
            await _settlementModuleApi.ReleaseCreditAsync(
                order.BuyerId, order.Id.Value, walletReserved, order.Currency, cancellationToken);
        }
        catch (Exception)
        {
            // Rattrapé par le balayeur d'abandon SI la commande a été écrite. Si elle
            // ne l'a pas été, la cagnotte reste engagée : c'est le seul trou résiduel
            // de ce chemin, et il exige que DEUX opérations échouent coup sur coup.
        }
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

        // Le résultat se lit ici aussi : une confirmation qui ne solde pas le stock
        // vend deux fois le même article. Voir l'encadré de
        // `ConfirmOrderPaymentCommandHandler`, même défaut, même correction.
        foreach (var line in order.Lines)
        {
            var settled = await _inventoryModuleApi.ConfirmReservationAsync(
                line.Sku, line.ShipFromLocationId, order.Id.Value, cancellationToken);

            if (!settled)
            {
                return Result.Failure(Error.Conflict(
                    "ordering.reservation_not_settled",
                    $"Le stock de « {line.Sku} » n'a pas pu être soldé pour cette commande."));
            }
        }

        var paid = order.MarkPaid();
        if (paid.IsFailure)
        {
            return paid;
        }

        return order.Confirm();
    }
}
