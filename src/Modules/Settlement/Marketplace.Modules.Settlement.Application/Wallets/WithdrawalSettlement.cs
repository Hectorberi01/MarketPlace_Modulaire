using Marketplace.Modules.Payments.Contracts;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Application.Wallets;

/// <summary>
/// Applique à un retrait le statut RÉEL de son dépôt chez le PSP. Point d'entrée
/// UNIQUE, partagé par les deux sources de vérité :
///  • le webhook (temps réel, mais peut se perdre ou arriver en désordre) ;
///  • la réconciliation périodique (lente, mais exhaustive — le filet de sécurité).
///
/// Les deux chemins doivent aboutir EXACTEMENT au même effet comptable, sinon on
/// obtiendrait un double remboursement (webhook « failed » suivi du même verdict en
/// réconciliation). D'où cette classe : la règle de clôture n'existe qu'à un endroit,
/// et elle est idempotente — un retrait déjà tranché n'est plus jamais touché.
/// </summary>
public sealed class WithdrawalSettlement
{
    private readonly WalletMutations _mutations;

    public WithdrawalSettlement(WalletMutations mutations) => _mutations = mutations;

    /// <summary>
    /// Renvoie <c>true</c> si le retrait a été tranché (clôturé ou remboursé) par cet appel.
    ///
    /// Garde d'idempotence : seul un retrait en <see cref="WithdrawalStatus.Processing"/>
    /// est mutable. Un retrait déjà Completed/Failed est ignoré — c'est ce qui rend sûr
    /// le fait de recevoir DEUX fois le même verdict (webhook rejoué + réconciliation).
    /// </summary>
    public async Task<bool> ApplyAsync(Withdrawal withdrawal, PayoutProgress progress, CancellationToken cancellationToken = default)
    {
        if (!withdrawal.IsProcessing)
        {
            return false;
        }

        switch (progress)
        {
            // Seule preuve de versement : c'est l'unique chemin vers Completed.
            case PayoutProgress.Sent:
                withdrawal.Complete(withdrawal.ProviderRef);
                return true;

            // Échec CONFIRMÉ par le PSP : l'argent n'est pas parti → on recrédite.
            case PayoutProgress.Failed:
                await RefundAsync(withdrawal, cancellationToken);
                return true;

            // pending / started / processing / unknown : encore en vol, ou PSP muet.
            // On ne touche à rien : ni clôture (le vendeur n'a rien reçu), ni
            // remboursement (l'argent est peut-être en route).
            default:
                return false;
        }
    }

    /// <summary>Échec confirmé : le retrait est marqué Failed et les fonds recrédités.</summary>
    private async Task RefundAsync(Withdrawal withdrawal, CancellationToken cancellationToken)
    {
        withdrawal.Fail("Versement refusé par le prestataire (statut « failed »).");

        // Le portefeuille est retrouvé par le PROPRIÉTAIRE du retrait, pas par un type
        // codé en dur : la même clôture vaut pour un vendeur, un livreur ou un client.
        await _mutations.CreditAsync(
            withdrawal.OwnerType, withdrawal.OwnerId, WalletAccount.Available, withdrawal.Amount,
            withdrawal.Currency, "withdrawal_refund", "withdrawal", withdrawal.Id.Value, cancellationToken);
    }
}
