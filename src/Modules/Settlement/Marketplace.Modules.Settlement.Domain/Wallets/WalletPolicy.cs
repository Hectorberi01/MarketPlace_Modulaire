using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Settlement.Domain.Wallets;

/// <summary>
/// Les règles qui distinguent les quatre acteurs — et elles se réduisent à deux
/// questions : quels sous-comptes cet acteur possède-t-il, et lesquels ont le droit
/// de passer sous zéro.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LA RÈGLE DU NÉGATIF, ÉNONCÉE UNE FOIS POUR TOUTES.
///
/// Un solde peut devenir négatif quand il représente une dette de L'ACTEUR envers la
/// plateforme. Il ne le peut JAMAIS quand il représente une dette de la PLATEFORME
/// envers l'acteur.
///
/// Concrètement : un vendeur déjà payé pour un article qui lui revient DOIT pouvoir
/// passer en négatif — refuser le débit laisserait la perte à la charge de la
/// plateforme, qui aurait remboursé le client ET payé le vendeur pour une vente
/// annulée. Un client, lui, ne peut pas devoir de l'argent à la marketplace : sa
/// cagnotte est de l'argent qu'on lui doit, pas l'inverse. La laisser passer sous zéro
/// n'aurait aucun sens comptable et ouvrirait un découvert que personne n'a consenti.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class WalletPolicy
{
    private static readonly IReadOnlyDictionary<WalletOwnerType, WalletAccount[]> Accounts =
        new Dictionary<WalletOwnerType, WalletAccount[]>
        {
            // Vendeur et livreur ont exactement le même cycle : on gagne « à venir », on
            // libère à la livraison, on retire depuis le disponible. C'est ce qui permet
            // au retrait de n'exister qu'en un seul exemplaire (voir Withdrawal).
            [WalletOwnerType.Seller] = new[] { WalletAccount.Pending, WalletAccount.Available },
            [WalletOwnerType.Courier] = new[] { WalletAccount.Pending, WalletAccount.Available },

            // Le client a un disponible (sa cagnotte) et un RÉSERVÉ. Ce second compte
            // n'est pas décoratif : sans lui, deux onglets ouverts permettent d'engager
            // deux fois la même cagnotte sur deux commandes, et l'une des deux se paie
            // avec de l'argent qui n'existe plus.
            [WalletOwnerType.Customer] = new[] { WalletAccount.Available, WalletAccount.Reserved },

            // La plateforme n'a pas de solde d'acteur : elle a des postes de résultat.
            [WalletOwnerType.Platform] = new[]
            {
                WalletAccount.Commission, WalletAccount.Provider,
                WalletAccount.Shipping, WalletAccount.Refunds
            }
        };

    public static IReadOnlyList<WalletAccount> AccountsFor(WalletOwnerType ownerType)
        => Accounts.TryGetValue(ownerType, out var accounts) ? accounts : Array.Empty<WalletAccount>();

    public static bool Owns(WalletOwnerType ownerType, WalletAccount account)
        => AccountsFor(ownerType).Contains(account);

    /// <summary>
    /// Ce solde a-t-il le droit de passer sous zéro ? Voir l'énoncé de la règle en tête
    /// de classe : la réponse dépend de QUI doit à QUI.
    /// </summary>
    public static bool AllowsNegative(WalletOwnerType ownerType, WalletAccount account)
        => ownerType switch
        {
            // Dette du vendeur / du livreur envers la plateforme après contre-passation.
            WalletOwnerType.Seller => account == WalletAccount.Available,
            WalletOwnerType.Courier => account == WalletAccount.Available,

            // Postes de résultat : un négatif est une dette de la plateforme envers
            // elle-même, qui se résorbe sur l'activité suivante. La masquer serait pire
            // que l'afficher.
            WalletOwnerType.Platform => true,

            // Le client, jamais.
            _ => false
        };

    public static Error UnknownAccount(WalletOwnerType ownerType, WalletAccount account)
        => Error.Validation(
            "wallet.account_not_owned",
            $"Le compte « {account} » n'existe pas pour un portefeuille de type « {ownerType} ».");
}
