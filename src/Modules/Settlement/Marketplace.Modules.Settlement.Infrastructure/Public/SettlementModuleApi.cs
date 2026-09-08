using Microsoft.Extensions.Logging;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Application.Wallets;
using Marketplace.Modules.Settlement.Contracts;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique de Settlement.
///
/// Elle PERSISTE elle-même : appelée depuis le Saga de commande, qui vit dans un autre
/// module et un autre DbContext, elle ne peut pas compter sur l'unité de travail de
/// l'appelant. C'est le même modèle de cohérence que la réservation de stock — deux
/// transactions successives, chacune compensable — et pas un relâchement de plus.
/// </summary>
internal sealed class SettlementModuleApi : ISettlementModuleApi
{
    private readonly IWalletRepository _wallets;
    private readonly WalletMutations _mutations;
    private readonly ISettlementUnitOfWork _unitOfWork;
    private readonly ILogger<SettlementModuleApi> _logger;

    public SettlementModuleApi(
        IWalletRepository wallets,
        WalletMutations mutations,
        ISettlementUnitOfWork unitOfWork,
        ILogger<SettlementModuleApi> logger)
    {
        _wallets = wallets;
        _mutations = mutations;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<decimal> GetSpendableCreditAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var wallet = await _wallets.GetAsync(WalletOwnerType.Customer, customerId, cancellationToken);

        // Le RÉSERVÉ n'est délibérément pas compté : il est déjà engagé sur une commande
        // en cours. L'inclure afficherait au client un montant qu'il ne peut pas utiliser.
        return wallet?.Balance(WalletAccount.Available) ?? 0m;
    }

    public async Task<decimal> TryReserveCreditAsync(
        Guid customerId, Guid orderId, decimal amount, string currency, CancellationToken cancellationToken = default)
    {
        if (amount <= 0m)
        {
            return 0m;
        }

        // Le transfert est PLAFONNÉ AU SOLDE par le domaine, il n'échoue pas. C'est ce
        // qui rend inutile un contrôle « ai-je assez ? » suivi d'un débit : entre les
        // deux, un autre onglet aurait pu engager la même cagnotte, et le contrôle
        // aurait menti.
        var moved = await _mutations.TransferAsync(
            WalletOwnerType.Customer, customerId, WalletAccount.Available, WalletAccount.Reserved,
            amount, currency, "checkout_reserve",
            WalletMutations.CheckoutReserveReferenceType, orderId, cancellationToken);

        if (moved <= 0m)
        {
            return 0m;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (moved < amount)
        {
            _logger.LogInformation(
                "Commande {OrderId} : cagnotte engagée à hauteur de {Moved} sur {Requested} demandés — solde insuffisant.",
                orderId, moved, amount);
        }

        return moved;
    }

    public async Task ConfirmCreditAsync(
        Guid customerId, Guid orderId, decimal amount, string currency, CancellationToken cancellationToken = default)
    {
        if (amount <= 0m)
        {
            return;
        }

        // DÉJÀ DÉPENSÉ : rejeu de l'événement « paiement encaissé ». On ne débite pas
        // deux fois.
        if (await _mutations.HasLedgerEntryAsync(WalletMutations.CheckoutSpendReferenceType, orderId, cancellationToken))
        {
            return;
        }

        // DÉJÀ LIBÉRÉ : la réservation a été rendue au disponible (annulation arrivée
        // avant). Débiter maintenant reprendrait au client de l'argent qui ne finance
        // plus rien — c'est l'ordre inverse des messages, et l'outbox ne le garantit pas.
        if (await _mutations.HasLedgerEntryAsync(WalletMutations.CheckoutReleaseReferenceType, orderId, cancellationToken))
        {
            _logger.LogError(
                "Commande {OrderId} : cagnotte déjà libérée, confirmation ignorée. La commande a été " +
                "confirmée APRÈS avoir été compensée — à examiner.",
                orderId);
            return;
        }

        var debit = await _mutations.DebitAsync(
            WalletOwnerType.Customer, customerId, WalletAccount.Reserved, amount, currency,
            "checkout_spend", WalletMutations.CheckoutSpendReferenceType, orderId, cancellationToken);

        if (debit.IsFailure)
        {
            // Le réservé ne couvre pas le montant : la commande a été confirmée pour une
            // cagnotte qui n'a pas été engagée. On ne force pas — la politique du client
            // interdit le négatif, et la contourner ici créerait la dette que le
            // portefeuille refuse par principe.
            _logger.LogError(
                "Commande {OrderId} : impossible de consommer {Amount} de cagnotte réservée ({Code}). " +
                "La commande est confirmée sans que la cagnotte soit débitée — à traiter à la main.",
                orderId, amount, debit.Error.Code);
            return;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseCreditAsync(
        Guid customerId, Guid orderId, decimal amount, string currency, CancellationToken cancellationToken = default)
    {
        if (amount <= 0m)
        {
            return;
        }

        if (await _mutations.HasLedgerEntryAsync(WalletMutations.CheckoutReleaseReferenceType, orderId, cancellationToken))
        {
            return;
        }

        // DÉJÀ DÉPENSÉ : la commande a été confirmée. Rendre la cagnotte maintenant
        // donnerait au client une commande payée ET son argent.
        if (await _mutations.HasLedgerEntryAsync(WalletMutations.CheckoutSpendReferenceType, orderId, cancellationToken))
        {
            _logger.LogError(
                "Commande {OrderId} : cagnotte déjà consommée, libération refusée — elle aurait rendu " +
                "au client de l'argent déjà dépensé.",
                orderId);
            return;
        }

        var moved = await _mutations.TransferAsync(
            WalletOwnerType.Customer, customerId, WalletAccount.Reserved, WalletAccount.Available,
            amount, currency, "checkout_release",
            WalletMutations.CheckoutReleaseReferenceType, orderId, cancellationToken);

        if (moved > 0m)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
