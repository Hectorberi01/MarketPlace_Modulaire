using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Application.Wallets;

/// <summary>
/// Point de passage unique de tout mouvement d'argent dans le module. Chaque méthode
/// modifie un solde ET écrit la ligne de grand livre correspondante — jamais l'un sans
/// l'autre. NE persiste PAS : l'Unit of Work du handler appelant commite.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// L'INVARIANT QUE CETTE CLASSE FAIT TENIR.
///
///     Pour chaque (portefeuille, compte) : somme des crédits − somme des débits = solde.
///
/// Il paraît évident. Il était pourtant FAUX avant cette réécriture, et sur un cas très
/// ordinaire : la libération d'un gain à la livraison déplaçait le montant du solde à
/// venir vers le disponible dans l'agrégat, mais n'écrivait au grand livre qu'une SEULE
/// ligne — le crédit du disponible. Le débit du solde à venir n'était consigné nulle
/// part. Le relevé d'un vendeur affichait donc des crédits sur « à venir » que rien ne
/// venait jamais solder, et la somme des écritures s'éloignait un peu plus du solde à
/// chaque livraison.
///
/// Un transfert écrit désormais DEUX lignes, parce qu'un transfert EST deux écritures.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// Le cache par requête (service « scoped ») évite de recréer deux fois un même
/// portefeuille non encore persisté — commission puis frais de livraison sur la même
/// commande — et l'insertion en double qui en résulterait.
/// </summary>
public sealed class WalletMutations
{
    private readonly IWalletRepository _wallets;
    private readonly IWalletTransactionRepository _ledger;

    private readonly Dictionary<(WalletOwnerType OwnerType, Guid OwnerId), Wallet> _cache = new();

    public WalletMutations(IWalletRepository wallets, IWalletTransactionRepository ledger)
    {
        _wallets = wallets;
        _ledger = ledger;
    }

    /// <summary>
    /// LA RÉFÉRENCE D'UNE CONTRE-PASSATION EST LE REMBOURSEMENT, PAS LA COMMANDE.
    ///
    /// Ces écritures portaient « order / orderId ». Deux retours distincts sur une même
    /// commande produisaient alors des lignes INDISTINGUABLES au grand livre : impossible
    /// d'y lire lequel avait déjà été contre-passé, ni d'expliquer à un vendeur pourquoi
    /// son solde a bougé deux fois. Elles portent « refund / returnRequestId », ce qui les
    /// rend uniques et sert de registre d'idempotence.
    /// </summary>
    public const string RefundReferenceType = "refund";

    /// <summary>
    /// Référence du CRÉDIT de cagnotte, distincte de celle de la contre-passation.
    ///
    /// Un même retour produit deux mouvements de sens opposé : le client est crédité,
    /// le vendeur est débité. Les faire porter la même référence rendrait le verrou
    /// d'idempotence de l'un sensible aux rejeux de l'autre — le premier arrivé
    /// bloquerait le second, et l'un des deux côtés du remboursement n'aurait jamais
    /// lieu.
    /// </summary>
    public const string ReturnRefundReferenceType = "return_refund";

    // ─────────────────────────────────────────────────────────────────────────────
    // TROIS RÉFÉRENCES DISTINCTES POUR UN MÊME IDENTIFIANT DE COMMANDE.
    //
    // Engager la cagnotte, la dépenser, la rendre : trois mouvements sur la même
    // commande. Leur donner une référence commune ferait que le premier arrivé
    // marquerait la commande « déjà traitée » et bloquerait les deux autres — le client
    // verrait son argent engagé et jamais débité, ou libéré deux fois.
    //
    // C'est la leçon du remboursement de retour, où crédit client et contre-passation
    // vendeur partageaient la même référence.
    // ─────────────────────────────────────────────────────────────────────────────
    public const string CheckoutReserveReferenceType = "checkout_reserve";
    public const string CheckoutSpendReferenceType = "checkout_spend";
    public const string CheckoutReleaseReferenceType = "checkout_release";

    /// <summary>
    /// Contre-passation d'une commande ANNULÉE après avoir été comptabilisée. Distincte
    /// de celle d'un retour : une commande peut être annulée puis, plus tard, faire
    /// l'objet d'un retour sur une autre de ses lignes.
    /// </summary>
    public const string OrderCancelledReferenceType = "order_cancelled";

    /// <summary>Rémunération d'une course livrée, référencée par l'identifiant de la course.</summary>
    public const string CourierDeliveryReferenceType = "courier_delivery";

    /// <summary>Une écriture existe-t-elle déjà pour cette référence ? (verrou de rejeu)</summary>
    public Task<bool> HasLedgerEntryAsync(string referenceType, Guid referenceId, CancellationToken ct)
        => _ledger.ExistsForReferenceAsync(referenceType, referenceId, ct);

    // ─────────────────────────────────────────────────────────────────────────────
    // Primitives génériques. Tout le reste du fichier n'en est qu'un habillage nommé.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Crédite un compte et consigne l'écriture.</summary>
    public async Task CreditAsync(
        WalletOwnerType ownerType, Guid ownerId, WalletAccount account, decimal amount,
        string currency, string reason, string referenceType, Guid referenceId, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreateAsync(ownerType, ownerId, currency, ct);
        var applied = wallet.Credit(account, amount);
        if (applied.IsFailure)
        {
            return;
        }

        await _ledger.AddAsync(
            WalletTransaction.For(wallet, account, WalletDirection.Credit, amount, reason, referenceType, referenceId), ct);
    }

    /// <summary>
    /// Débite un compte et consigne l'écriture. Si la politique de l'acteur refuse le
    /// négatif et que le solde est insuffisant, RIEN n'est écrit — ni solde, ni ligne.
    /// </summary>
    public async Task<Result> DebitAsync(
        WalletOwnerType ownerType, Guid ownerId, WalletAccount account, decimal amount,
        string currency, string reason, string referenceType, Guid referenceId, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return Result.Success();
        }

        var wallet = await GetOrCreateAsync(ownerType, ownerId, currency, ct);
        var applied = wallet.Debit(account, amount);
        if (applied.IsFailure)
        {
            return applied;
        }

        await _ledger.AddAsync(
            WalletTransaction.For(wallet, account, WalletDirection.Debit, amount, reason, referenceType, referenceId), ct);
        return Result.Success();
    }

    /// <summary>
    /// Déplace un montant d'un compte vers un autre, plafonné au solde source, et écrit
    /// LES DEUX écritures — le débit de la source et le crédit de la destination.
    /// </summary>
    public async Task<decimal> TransferAsync(
        WalletOwnerType ownerType, Guid ownerId, WalletAccount from, WalletAccount to, decimal amount,
        string currency, string reason, string referenceType, Guid referenceId, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return 0m;
        }

        var wallet = await GetOrCreateAsync(ownerType, ownerId, currency, ct);
        var moved = wallet.Transfer(from, to, amount);
        if (moved <= 0m)
        {
            return 0m;
        }

        await _ledger.AddAsync(
            WalletTransaction.For(wallet, from, WalletDirection.Debit, moved, reason, referenceType, referenceId), ct);
        await _ledger.AddAsync(
            WalletTransaction.For(wallet, to, WalletDirection.Credit, moved, reason, referenceType, referenceId), ct);

        return moved;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Vendeur
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Crédite le solde à venir du vendeur (gain net d'une commande confirmée).</summary>
    public Task CreditSellerPendingAsync(Guid sellerId, decimal netAmount, string currency, Guid orderId, CancellationToken ct)
        => CreditAsync(WalletOwnerType.Seller, sellerId, WalletAccount.Pending, netAmount, currency,
            "order_confirmed", "order", orderId, ct);

    /// <summary>Libère un gain à la livraison : du solde à venir vers le disponible.</summary>
    public Task ReleaseSellerAsync(Guid sellerId, decimal netAmount, string currency, Guid orderId, CancellationToken ct)
        => TransferAsync(WalletOwnerType.Seller, sellerId, WalletAccount.Pending, WalletAccount.Available,
            netAmount, currency, "delivery_release", "order", orderId, ct);

    public async Task<bool> RefundAlreadyReversedAsync(Guid returnRequestId, CancellationToken ct)
        => await _ledger.ExistsForReferenceAsync(RefundReferenceType, returnRequestId, ct);

    /// <summary>
    /// CONTRE-PASSATION : reprend au vendeur le gain d'une vente remboursée.
    ///
    /// Deux écritures distinctes au grand livre — solde à venir, puis disponible — et non
    /// une seule globale : c'est la seule façon d'expliquer plus tard, ligne à ligne,
    /// POURQUOI le solde d'un vendeur a bougé. Un débit unique et opaque se solde toujours
    /// par une discussion qu'on ne peut pas gagner.
    ///
    /// Le portefeuille est CRÉÉ s'il n'existe pas : un vendeur peut être contre-passé
    /// avant d'avoir le moindre solde. Refuser le débit ferait disparaître la dette au
    /// lieu de l'enregistrer.
    /// </summary>
    public Task DebitSellerForRefundAsync(
        Guid sellerId, decimal netAmount, string currency, Guid returnRequestId, CancellationToken ct)
        => DebitSellerCascadeAsync(
            sellerId, netAmount, currency, "refund_reversal", RefundReferenceType, returnRequestId, ct);

    /// <summary>
    /// Reprend au vendeur le gain d'une commande ANNULÉE après avoir été comptabilisée.
    /// Même mécanique que la contre-passation d'un retour, référence distincte.
    /// </summary>
    public Task DebitSellerForCancellationAsync(
        Guid sellerId, decimal netAmount, string currency, Guid orderId, CancellationToken ct)
        => DebitSellerCascadeAsync(
            sellerId, netAmount, currency, "order_cancelled", OrderCancelledReferenceType, orderId, ct);

    private async Task DebitSellerCascadeAsync(
        Guid sellerId, decimal netAmount, string currency,
        string reason, string referenceType, Guid referenceId, CancellationToken ct)
    {
        if (netAmount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreateAsync(WalletOwnerType.Seller, sellerId, currency, ct);
        var (fromPending, fromAvailable) = wallet.DebitCascade(
            WalletAccount.Pending, WalletAccount.Available, netAmount);

        if (fromPending > 0m)
        {
            await _ledger.AddAsync(WalletTransaction.For(
                wallet, WalletAccount.Pending, WalletDirection.Debit, fromPending,
                reason, referenceType, referenceId), ct);
        }

        if (fromAvailable > 0m)
        {
            await _ledger.AddAsync(WalletTransaction.For(
                wallet, WalletAccount.Available, WalletDirection.Debit, fromAvailable,
                reason, referenceType, referenceId), ct);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Client
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Crédite la cagnotte d'un client du montant d'un remboursement de retour.
    ///
    /// Le portefeuille est créé à la volée : un client n'en a aucun avant son premier
    /// remboursement, et c'est le cas NORMAL, pas une anomalie.
    /// </summary>
    public Task CreditCustomerRefundAsync(
        Guid customerId, decimal amount, string currency, Guid returnRequestId, CancellationToken ct)
        => CreditAsync(WalletOwnerType.Customer, customerId, WalletAccount.Available, amount, currency,
            "return_refund", ReturnRefundReferenceType, returnRequestId, ct);

    /// <summary>
    /// La cagnotte a-t-elle DÉJÀ été créditée pour ce retour ? Verrou d'idempotence du
    /// crédit client, symétrique de celui de la contre-passation vendeur.
    /// </summary>
    public Task<bool> CustomerAlreadyRefundedAsync(Guid returnRequestId, CancellationToken ct)
        => _ledger.ExistsForReferenceAsync(ReturnRefundReferenceType, returnRequestId, ct);

    // ─────────────────────────────────────────────────────────────────────────────
    // Coursier
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Paie une course livrée : crédite le coursier ET débite les frais de livraison
    /// encaissés par la plateforme, en une seule opération.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LES DEUX ÉCRITURES VONT ENSEMBLE, ET C'EST TOUT L'INTÉRÊT.
    ///
    /// La plateforme a encaissé un forfait de livraison à la commande (compte
    /// « Shipping »). Payer le coursier sans y toucher ferait grossir indéfiniment un
    /// solde qui ne correspond à rien : la plateforme afficherait des frais de livraison
    /// jamais dépensés, alors même qu'elle paie des coursiers avec.
    ///
    /// SI LE COMPTE PASSE EN NÉGATIF, C'EST UNE INFORMATION, PAS UNE PANNE. La politique
    /// de la plateforme l'autorise, et ce négatif dit exactement une chose : les forfaits
    /// facturés aux acheteurs ne couvrent pas ce que coûtent les courses. C'est le seul
    /// endroit du système où cette vérité devient visible — et il vaut mieux la voir sur
    /// un écran que la découvrir en fin d'exercice.
    ///
    /// LE CRÉDIT VA DIRECTEMENT AU DISPONIBLE, pas au « à venir ». Le vendeur a un solde
    /// à venir parce que son gain est retenu jusqu'à la livraison ; pour le coursier, la
    /// livraison EST l'événement. Le mettre en attente demanderait un déclencheur pour
    /// l'en sortir, et un séquestre sans libération, c'est de l'argent bloqué pour
    /// toujours — la panne qu'on vient de corriger sur les commandes abandonnées.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task PayCourierForDeliveryAsync(
        Guid courierId, decimal amount, string currency, Guid deliveryJobId, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return;
        }

        await CreditAsync(
            WalletOwnerType.Courier, courierId, WalletAccount.Available, amount, currency,
            "courier_delivery", CourierDeliveryReferenceType, deliveryJobId, ct);

        await DebitPlatformAsync(
            WalletAccount.Shipping, amount, currency,
            "courier_delivery", CourierDeliveryReferenceType, deliveryJobId, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Plateforme
    // ─────────────────────────────────────────────────────────────────────────────

    public Task CreditPlatformCommissionAsync(decimal amount, string currency, Guid orderId, CancellationToken ct)
        => CreditPlatformAsync(WalletAccount.Commission, amount, currency, "commission", "order", orderId, ct);

    public Task CreditPlatformProviderFeeAsync(decimal amount, string currency, Guid orderId, CancellationToken ct)
        => CreditPlatformAsync(WalletAccount.Provider, amount, currency, "provider_fee", "order", orderId, ct);

    public Task CreditPlatformShippingAsync(decimal amount, string currency, Guid orderId, CancellationToken ct)
        => CreditPlatformAsync(WalletAccount.Shipping, amount, currency, "shipping_fee", "order", orderId, ct);

    public Task DebitPlatformCommissionAsync(decimal amount, string currency, Guid returnRequestId, CancellationToken ct)
        => DebitPlatformAsync(WalletAccount.Commission, amount, currency, "refund_reversal", RefundReferenceType, returnRequestId, ct);

    public Task DebitPlatformProviderFeeAsync(decimal amount, string currency, Guid returnRequestId, CancellationToken ct)
        => DebitPlatformAsync(WalletAccount.Provider, amount, currency, "refund_reversal", RefundReferenceType, returnRequestId, ct);

    /// <summary>Restitue les encaissements plateforme d'une commande annulée après comptabilisation.</summary>
    public async Task DebitPlatformForCancellationAsync(
        decimal commission, decimal providerFee, decimal shippingFee, string currency, Guid orderId, CancellationToken ct)
    {
        await DebitPlatformAsync(WalletAccount.Commission, commission, currency, "order_cancelled", OrderCancelledReferenceType, orderId, ct);
        await DebitPlatformAsync(WalletAccount.Provider, providerFee, currency, "order_cancelled", OrderCancelledReferenceType, orderId, ct);

        // LES FRAIS DE LIVRAISON AUSSI, contrairement au remboursement d'un retour.
        // Sur un retour, la marchandise a voyagé : le transport a été rendu, il reste
        // acquis. Une commande annulée avant paiement n'a jamais rien fait bouger — les
        // garder serait encaisser une livraison qui n'a pas eu lieu.
        await DebitPlatformAsync(WalletAccount.Shipping, shippingFee, currency, "order_cancelled", OrderCancelledReferenceType, orderId, ct);
    }

    /// <summary>
    /// Comptabilise un remboursement DIRECT versé à un client (coût plateforme).
    /// Référencé par l'identifiant du remboursement — idempotence et traçabilité.
    /// </summary>
    public Task AccrueCustomerRefundAsync(decimal amount, string currency, Guid refundId, CancellationToken ct)
        => CreditPlatformAsync(WalletAccount.Refunds, amount, currency, "customer_refund", "customer_refund", refundId, ct);

    /// <summary>Contre-passe un remboursement client dont le payout a définitivement échoué.</summary>
    public Task ReverseCustomerRefundAsync(decimal amount, string currency, Guid refundId, CancellationToken ct)
        => DebitPlatformAsync(WalletAccount.Refunds, amount, currency, "customer_refund_reversal", "customer_refund", refundId, ct);

    private Task CreditPlatformAsync(
        WalletAccount account, decimal amount, string currency, string reason,
        string referenceType, Guid referenceId, CancellationToken ct)
        => CreditAsync(WalletOwnerType.Platform, Wallet.PlatformOwnerId, account, amount, currency, reason, referenceType, referenceId, ct);

    private Task DebitPlatformAsync(
        WalletAccount account, decimal amount, string currency, string reason,
        string referenceType, Guid referenceId, CancellationToken ct)
        => DebitAsync(WalletOwnerType.Platform, Wallet.PlatformOwnerId, account, amount, currency, reason, referenceType, referenceId, ct);

    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Charge le portefeuille, ou le crée s'il n'existe pas encore. La création à la
    /// volée est délibérée : aucun acteur n'a de portefeuille avant son premier
    /// mouvement, et exiger une création préalable ferait échouer des écritures
    /// parfaitement légitimes — un vendeur contre-passé avant sa première vente libérée,
    /// par exemple.
    /// </summary>
    public async Task<Wallet> GetOrCreateAsync(
        WalletOwnerType ownerType, Guid ownerId, string currency, CancellationToken ct)
    {
        var key = (ownerType, ownerId);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var wallet = await _wallets.GetAsync(ownerType, ownerId, ct);
        if (wallet is null)
        {
            wallet = Wallet.Create(ownerType, ownerId, currency);
            await _wallets.AddAsync(wallet, ct);
        }

        _cache[key] = wallet;
        return wallet;
    }
}
