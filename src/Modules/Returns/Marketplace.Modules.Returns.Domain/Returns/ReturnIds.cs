namespace Marketplace.Modules.Returns.Domain.Returns;

/// <summary>Identité forte d'une demande de retour.</summary>
public readonly record struct ReturnRequestId(Guid Value)
{
    public static ReturnRequestId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Motif d'un retour.</summary>
public enum ReturnReason
{
    Defective = 0,
    NotAsDescribed = 1,
    ChangedMind = 2,
    WrongItem = 3,
    Other = 4
}

/// <summary>Cycle de vie d'une demande de retour (RMA).</summary>
public enum ReturnStatus
{
    Requested = 0,
    Approved = 1,
    Rejected = 2,
    Received = 3,

    /// <summary>
    /// Remboursement VALIDÉ, argent PAS ENCORE PARTI.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// CET ÉTAT EST LA CORRECTION D'UN BUG GRAVE.
    ///
    /// Avant, le vendeur cliquait « Rembourser » et le retour passait directement
    /// à « Remboursé ». Or rien n'appelait la banque : aucun franc ne bougeait.
    /// La base affirmait donc qu'un client avait été remboursé alors qu'il n'avait
    /// rien reçu — et l'on ne s'en apercevait que par sa réclamation.
    ///
    /// FedaPay n'expose AUCUN endpoint de remboursement (leur documentation est
    /// formelle : cela se fait depuis leur tableau de bord, et uniquement en MTN
    /// Mobile Money). Le versement est donc, par nécessité, un geste humain.
    ///
    /// D'où cet état intermédiaire : il rend cette attente VISIBLE. Un retour reste
    /// « en attente de versement » tant qu'un administrateur n'a pas exécuté
    /// l'opération chez FedaPay et saisi sa référence. On ne peut plus confondre
    /// « décidé » et « fait ».
    /// ─────────────────────────────────────────────────────────────────────────
    /// </summary>
    RefundPending = 5,

    /// <summary>
    /// Argent RÉELLEMENT versé, référence FedaPay à l'appui.
    ///
    /// ⚠️ Cet état ne doit JAMAIS être atteint sans preuve d'exécution. C'est la
    /// seule garantie que la base dise la vérité sur l'argent.
    /// </summary>
    Refunded = 4
}
