using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Application.Wallets;

/// <summary>Où verser, à qui, et sous quel nom — une fois le type d'acteur résolu.</summary>
public sealed record WithdrawalPayout(string Provider, string AccountNumber, string BeneficiaryName);

/// <summary>
/// Trouve le compte Mobile Money à créditer pour un retrait, QUEL QUE SOIT L'ACTEUR.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CE SERVICE EXISTE.
///
/// Le retrait est un seul objet — même cycle de vie, même retenue de fonds, même
/// validation admin, même payout, même réconciliation — mais les coordonnées de
/// versement ne vivent pas au même endroit selon qui retire : un vendeur les a
/// déclarées à son inscription (Sellers), un client les a enregistrées comme moyen
/// de paiement (Identity).
///
/// Sans ce point unique, la validation admin devrait porter un « if » par acteur, et
/// chaque nouvel acteur — le livreur arrive — rouvrirait le handler le plus sensible
/// du module : celui qui décide qu'un versement part. La variation est isolée ici,
/// et là seulement.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class WithdrawalPayoutResolver
{
    private readonly ISellerModuleApi _sellers;
    private readonly IIdentityModuleApi _identity;

    public WithdrawalPayoutResolver(ISellerModuleApi sellers, IIdentityModuleApi identity)
    {
        _sellers = sellers;
        _identity = identity;
    }

    /// <summary>
    /// Le compte de versement de cet acteur, ou null s'il n'en a pas d'utilisable.
    /// Null n'est pas une erreur technique : c'est un motif de refus légitime, que
    /// l'appelant transforme en échec de retrait avec remboursement des fonds retenus.
    /// </summary>
    public async Task<WithdrawalPayout?> ResolveAsync(
        WalletOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken)
        => ownerType switch
        {
            WalletOwnerType.Seller => await ResolveSellerAsync(ownerId, cancellationToken),
            WalletOwnerType.Customer => await ResolveCustomerAsync(ownerId, cancellationToken),

            // Le livreur n'a pas encore de module qui porte ses coordonnées ; la
            // plateforme, elle, ne retire jamais — ses soldes sont des postes de
            // résultat, pas de l'argent à sortir.
            _ => null
        };

    private async Task<WithdrawalPayout?> ResolveSellerAsync(Guid sellerId, CancellationToken ct)
    {
        var seller = await _sellers.GetSellerAsync(sellerId, ct);
        var account = seller?.Payout;

        if (account is null || !WalletPayout.IsMobileMoney(account.Provider) || string.IsNullOrWhiteSpace(account.AccountNumber))
        {
            return null;
        }

        return new WithdrawalPayout(
            account.Provider,
            account.AccountNumber,
            string.IsNullOrWhiteSpace(account.AccountName) ? seller!.ShopName : account.AccountName);
    }

    private async Task<WithdrawalPayout?> ResolveCustomerAsync(Guid customerId, CancellationToken ct)
    {
        var account = await _identity.GetDefaultPayoutAccountAsync(customerId, ct);

        if (account is null || !WalletPayout.IsMobileMoney(account.Provider) || string.IsNullOrWhiteSpace(account.AccountNumber))
        {
            return null;
        }

        return new WithdrawalPayout(account.Provider, account.AccountNumber, account.AccountName);
    }
}
