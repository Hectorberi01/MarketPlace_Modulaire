namespace Marketplace.Modules.Settlement.Domain.Wallets;

/// <summary>Identité forte d'un portefeuille, quel que soit son propriétaire.</summary>
public readonly record struct WalletId(Guid Value)
{
    public static WalletId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Identité forte d'un retrait vendeur.</summary>
public readonly record struct WithdrawalId(Guid Value)
{
    public static WithdrawalId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Identité forte d'un remboursement client (versement MoMo initié par l'admin).</summary>
public readonly record struct CustomerRefundId(Guid Value)
{
    public static CustomerRefundId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Cycle de vie d'un remboursement client :
///  • Processing : payout FedaPay DEMANDÉ (start accepté ou issue indéterminée) ;
///    le portefeuille plateforme reste débité — on ne rembourse jamais tant qu'on
///    ne sait pas, sous peine de double versement.
///  • Completed  : versement confirmé par le PSP (statut « sent »).
///  • Failed     : rejet DÉFINITIF du PSP → le débit plateforme est contre-passé.
/// </summary>
public enum CustomerRefundStatus
{
    Processing = 0,
    Completed = 1,
    Failed = 2
}

/// <summary>
/// Par quel canal la somme est rendue au client.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI LES DEUX CANAUX PARTAGENT UNE SEULE TABLE.
///
/// Un remboursement direct peut se répartir entre un versement Mobile Money et un
/// crédit de cagnotte. La tentation est de ne persister que la jambe espèces et de
/// laisser l'autre vivre dans le grand livre — c'est un piège : le plafond « ne pas
/// rembourser plus que la valeur de la commande » se calcule en sommant cette table,
/// et une jambe absente de la table est une jambe que le plafond ne voit pas. Deux
/// appels successifs rembourseraient alors la commande une fois et demie, chaque
/// contrôle passant proprement.
///
/// Les deux jambes sont donc deux LIGNES, distinguées par ce discriminant. La jambe
/// cagnotte naît <see cref="CustomerRefundStatus.Completed"/> — il n'y a rien à
/// attendre d'un prestataire — et la réconciliation, qui ne regarde que les lignes
/// « en cours », ne la voit jamais.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public enum CustomerRefundChannel
{
    /// <summary>Versement Mobile Money : de l'argent sort réellement.</summary>
    Cash = 0,

    /// <summary>Crédit de cagnotte : la dette change de forme, la trésorerie ne bouge pas.</summary>
    Wallet = 1
}

/// <summary>
/// Les quatre acteurs qui détiennent un portefeuille. Persisté en CHAÎNE (voir la
/// configuration EF) : ajouter un acteur ne renumérote donc rien et ne demande aucune
/// reprise de données.
/// </summary>
public enum WalletOwnerType
{
    Seller = 0,
    Platform = 1,

    /// <summary>Acheteur : cagnotte alimentée par les remboursements.</summary>
    Customer = 2,

    /// <summary>Coursier : rémunération des courses livrées.</summary>
    Courier = 3
}

/// <summary>
/// Sous-compte visé par une écriture. Quels comptes appartiennent à quel acteur est
/// décidé par <see cref="WalletPolicy"/>, pas ici : cette énumération est le vocabulaire
/// commun du grand livre, la politique en est la grammaire.
///
/// Vendeur et livreur : Pending (à venir) et Available (retirable).
/// Client : Available (cagnotte) et Reserved (engagé sur une commande).
/// Plateforme : Commission, Provider, Shipping et Refunds.
/// </summary>
public enum WalletAccount
{
    Pending = 0,
    Available = 1,
    Commission = 2,
    Shipping = 3,
    Provider = 4,

    /// <summary>Plateforme : total reversé aux clients en remboursements directs (coût).</summary>
    Refunds = 5,

    /// <summary>
    /// Client : montant engagé sur une commande en cours de paiement, sorti du
    /// disponible mais pas encore consommé.
    ///
    /// Ce compte est ce qui empêche de dépenser deux fois la même cagnotte depuis deux
    /// onglets : l'argent quitte le disponible au moment où l'acheteur l'applique, et ne
    /// devient définitivement dépensé qu'à la confirmation du paiement. Si celui-ci
    /// échoue, le réservé revient au disponible.
    /// </summary>
    Reserved = 6
}

/// <summary>Sens d'une écriture (crédit augmente le solde, débit le diminue).</summary>
public enum WalletDirection
{
    Credit = 0,
    Debit = 1
}

/// <summary>
/// Cycle de vie d'un retrait :
///  • Requested : demandé par le vendeur, en attente de validation admin (fonds retenus).
///  • Completed : validé par l'admin et payé (payout FedaPay réussi).
///  • Failed    : validé mais échec du payout (fonds recrédités).
///  • Rejected  : refusé par l'admin (fonds recrédités).
/// (Pending : ancien statut des retraits auto — conservé pour les données historiques.)
/// </summary>
public enum WithdrawalStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
    Requested = 3,
    Rejected = 4,

    /// <summary>
    /// Versement DEMANDÉ au PSP mais pas encore confirmé. État essentiel : un « start »
    /// FedaPay accepté ne veut dire que « started », pas « reçu ». Les fonds restent
    /// débités du vendeur ; la réconciliation clôturera en Completed (statut « sent »)
    /// ou en Failed avec remboursement (statut « failed »).
    /// Couvre aussi les issues INDÉTERMINÉES (timeout) : on ne rembourse pas, car
    /// l'argent est peut-être parti — rembourser autoriserait un double versement.
    /// </summary>
    Processing = 5
}
