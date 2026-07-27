using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Application.Wallets;

/// <summary>
/// Service interne mutualisant les mouvements de portefeuille (vendeur et
/// plateforme) déclenchés par les events de commande, et l'écriture des lignes
/// au grand livre. NE persiste PAS : l'Unit of Work du handler appelant commite.
///
/// Un cache par requête (service « scoped ») évite de recréer deux fois un même
/// portefeuille non encore persisté (ex. commission puis frais de livraison sur
/// la même commande) et l'insertion en double qui en résulterait.
/// </summary>
public sealed class WalletMutations
{
    private readonly ISellerWalletRepository _sellerWallets;
    private readonly IPlatformWalletRepository _platformWallets;
    private readonly IWalletTransactionRepository _ledger;

    private readonly Dictionary<Guid, SellerWallet> _sellerCache = new();
    private PlatformWallet? _platform;

    public WalletMutations(
        ISellerWalletRepository sellerWallets,
        IPlatformWalletRepository platformWallets,
        IWalletTransactionRepository ledger)
    {
        _sellerWallets = sellerWallets;
        _platformWallets = platformWallets;
        _ledger = ledger;
    }

    /// <summary>Crédite le solde à venir du vendeur (gain net d'une commande confirmée).</summary>
    public async Task CreditSellerPendingAsync(Guid sellerId, decimal netAmount, string currency, Guid orderId, CancellationToken ct)
    {
        if (netAmount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreateSellerAsync(sellerId, currency, ct);
        wallet.CreditPending(netAmount);
        await _ledger.AddAsync(
            WalletTransaction.ForSeller(sellerId, WalletAccount.Pending, WalletDirection.Credit, netAmount, currency, "order_confirmed", "order", orderId), ct);
    }

    /// <summary>Déplace un montant du solde à venir vers le solde principal (livraison).</summary>
    public async Task ReleaseSellerAsync(Guid sellerId, decimal netAmount, string currency, Guid orderId, CancellationToken ct)
    {
        if (netAmount <= 0m)
        {
            return;
        }

        var wallet = await _sellerWallets.GetBySellerAsync(sellerId, ct);
        if (wallet is null)
        {
            return;
        }

        _sellerCache[sellerId] = wallet;
        wallet.ReleaseToAvailable(netAmount);
        await _ledger.AddAsync(
            WalletTransaction.ForSeller(sellerId, WalletAccount.Available, WalletDirection.Credit, netAmount, currency, "delivery_release", "order", orderId), ct);
    }

    /// <summary>Crédite le solde commission de la plateforme.</summary>
    public async Task CreditPlatformCommissionAsync(decimal amount, string currency, Guid orderId, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreatePlatformAsync(currency, ct);
        wallet.CreditCommission(amount);
        await _ledger.AddAsync(
            WalletTransaction.ForPlatform(WalletAccount.Commission, WalletDirection.Credit, amount, currency, "commission", "order", orderId), ct);
    }

    /// <summary>Crédite le solde frais provider de la plateforme.</summary>
    public async Task CreditPlatformProviderFeeAsync(decimal amount, string currency, Guid orderId, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreatePlatformAsync(currency, ct);
        wallet.CreditProviderFee(amount);
        await _ledger.AddAsync(
            WalletTransaction.ForPlatform(WalletAccount.Provider, WalletDirection.Credit, amount, currency, "provider_fee", "order", orderId), ct);
    }

    /// <summary>Crédite le solde frais de livraison de la plateforme.</summary>
    public async Task CreditPlatformShippingAsync(decimal amount, string currency, Guid orderId, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreatePlatformAsync(currency, ct);
        wallet.CreditShipping(amount);
        await _ledger.AddAsync(
            WalletTransaction.ForPlatform(WalletAccount.Shipping, WalletDirection.Credit, amount, currency, "shipping_fee", "order", orderId), ct);
    }

    /// <summary>
    /// CONTRE-PASSATION : reprend au vendeur le gain d'une vente remboursée.
    ///
    /// Deux écritures distinctes au grand livre (solde à venir, puis solde principal),
    /// et non une seule globale : c'est la seule façon de pouvoir expliquer plus tard,
    /// ligne à ligne, POURQUOI le solde d'un vendeur a bougé. Un débit unique et opaque
    /// se solde toujours par une discussion qu'on ne peut pas gagner.
    ///
    /// Le portefeuille est CRÉÉ s'il n'existe pas : un vendeur peut avoir été remboursé
    /// avant d'avoir le moindre solde. Refuser le débit dans ce cas ferait disparaître
    /// la dette au lieu de l'enregistrer.
    /// </summary>
    /// <summary>
    /// ⚠️ LA RÉFÉRENCE EST LE REMBOURSEMENT, PAS LA COMMANDE.
    ///
    /// Ces écritures portaient `("order", orderId)`. C'était un défaut : deux retours
    /// distincts sur une même commande produisaient des lignes INDISTINGUABLES au grand
    /// livre — impossible d'y lire lequel avait déjà été contre-passé, ni d'expliquer à
    /// un vendeur pourquoi son solde a bougé deux fois.
    ///
    /// Elles portent désormais `("refund", returnRequestId)`, ce qui les rend uniques
    /// ET sert de registre d'idempotence (voir ExistsForReferenceAsync).
    /// </summary>
    public const string RefundReferenceType = "refund";

    public async Task<bool> RefundAlreadyReversedAsync(Guid returnRequestId, CancellationToken ct)
        => await _ledger.ExistsForReferenceAsync(RefundReferenceType, returnRequestId, ct);

    public async Task DebitSellerForRefundAsync(
        Guid sellerId, decimal netAmount, string currency, Guid returnRequestId, CancellationToken ct)
    {
        if (netAmount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreateSellerAsync(sellerId, currency, ct);
        var (fromPending, fromAvailable) = wallet.DebitForRefund(netAmount);

        if (fromPending > 0m)
        {
            await _ledger.AddAsync(
                WalletTransaction.ForSeller(sellerId, WalletAccount.Pending, WalletDirection.Debit, fromPending, currency, "refund_reversal", RefundReferenceType, returnRequestId), ct);
        }

        if (fromAvailable > 0m)
        {
            await _ledger.AddAsync(
                WalletTransaction.ForSeller(sellerId, WalletAccount.Available, WalletDirection.Debit, fromAvailable, currency, "refund_reversal", RefundReferenceType, returnRequestId), ct);
        }
    }

    /// <summary>Restitue la commission de la plateforme sur une vente remboursée.</summary>
    public async Task DebitPlatformCommissionAsync(decimal amount, string currency, Guid returnRequestId, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreatePlatformAsync(currency, ct);
        wallet.DebitCommission(amount);
        await _ledger.AddAsync(
            WalletTransaction.ForPlatform(WalletAccount.Commission, WalletDirection.Debit, amount, currency, "refund_reversal", RefundReferenceType, returnRequestId), ct);
    }

    /// <summary>Restitue les frais provider de la plateforme sur une vente remboursée.</summary>
    public async Task DebitPlatformProviderFeeAsync(decimal amount, string currency, Guid returnRequestId, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreatePlatformAsync(currency, ct);
        wallet.DebitProviderFee(amount);
        await _ledger.AddAsync(
            WalletTransaction.ForPlatform(WalletAccount.Provider, WalletDirection.Debit, amount, currency, "refund_reversal", RefundReferenceType, returnRequestId), ct);
    }

    /// <summary>
    /// Comptabilise un remboursement DIRECT versé à un client (coût plateforme) : crédite
    /// le solde « refunds » du portefeuille plateforme et trace l'écriture. Référencé par
    /// l'identifiant du remboursement (idempotence + traçabilité au grand livre).
    /// </summary>
    public async Task AccrueCustomerRefundAsync(decimal amount, string currency, Guid refundId, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreatePlatformAsync(currency, ct);
        wallet.AccrueRefund(amount);
        await _ledger.AddAsync(
            WalletTransaction.ForPlatform(WalletAccount.Refunds, WalletDirection.Credit, amount, currency, "customer_refund", "customer_refund", refundId), ct);
    }

    /// <summary>Contre-passe un remboursement client comptabilisé dont le payout a échoué.</summary>
    public async Task ReverseCustomerRefundAsync(decimal amount, string currency, Guid refundId, CancellationToken ct)
    {
        if (amount <= 0m)
        {
            return;
        }

        var wallet = await GetOrCreatePlatformAsync(currency, ct);
        wallet.ReverseRefund(amount);
        await _ledger.AddAsync(
            WalletTransaction.ForPlatform(WalletAccount.Refunds, WalletDirection.Debit, amount, currency, "customer_refund_reversal", "customer_refund", refundId), ct);
    }

    private async Task<SellerWallet> GetOrCreateSellerAsync(Guid sellerId, string currency, CancellationToken ct)
    {
        if (_sellerCache.TryGetValue(sellerId, out var cached))
        {
            return cached;
        }

        var wallet = await _sellerWallets.GetBySellerAsync(sellerId, ct);
        if (wallet is null)
        {
            wallet = SellerWallet.Create(sellerId, currency);
            await _sellerWallets.AddAsync(wallet, ct);
        }

        _sellerCache[sellerId] = wallet;
        return wallet;
    }

    private async Task<PlatformWallet> GetOrCreatePlatformAsync(string currency, CancellationToken ct)
    {
        if (_platform is not null)
        {
            return _platform;
        }

        var wallet = await _platformWallets.GetAsync(ct);
        if (wallet is null)
        {
            wallet = PlatformWallet.Create(currency);
            await _platformWallets.AddAsync(wallet, ct);
        }

        _platform = wallet;
        return wallet;
    }
}
