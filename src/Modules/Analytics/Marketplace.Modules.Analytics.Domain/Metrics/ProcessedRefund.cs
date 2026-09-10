namespace Marketplace.Modules.Analytics.Domain.Metrics;

/// <summary>
/// Marqueur d'idempotence d'un remboursement déjà imputé aux métriques.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// POURQUOI PAS `ProcessedOrder`.
///
/// Ce marqueur-là a pour clé primaire l'identifiant de commande, et un seul par
/// commande est physiquement possible. Il ne peut donc porter ni l'accumulation ET la
/// déduction, ni DEUX déductions — or une même commande peut faire l'objet de
/// plusieurs retours, sur des offres différentes, plus un remboursement direct de
/// l'administrateur. Réutiliser cette table aurait fait disparaître le deuxième
/// remboursement en silence.
///
/// La clé est donc (source, identifiant) : chaque remboursement est identifié par ce
/// qui l'a produit — une demande de retour, un paiement, un remboursement client — et
/// deux remboursements distincts ne peuvent plus se confondre. Le rejeu d'un même
/// événement, lui, reste sans effet.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ProcessedRefund
{
    /// <summary>Retour produit remboursé (clé : identifiant de la demande de retour).</summary>
    public const string SourceRetour = "return";

    /// <summary>Paiement remboursé au prestataire (clé : identifiant du paiement).</summary>
    public const string SourcePaiement = "payment";

    /// <summary>Remboursement direct décidé par l'administrateur (clé : identifiant du remboursement).</summary>
    public const string SourceGesteCommercial = "customer-refund";

    private ProcessedRefund()
    {
    }

    private ProcessedRefund(string source, Guid sourceId, Guid orderId, decimal amount)
    {
        Source = source;
        SourceId = sourceId;
        OrderId = orderId;
        Amount = amount;
        ProcessedAtUtc = DateTime.UtcNow;
    }

    public string Source { get; private set; } = default!;
    public Guid SourceId { get; private set; }

    /// <summary>
    /// Commande concernée.
    ///
    /// Sans elle, la question « combien a-t-on déjà retranché sur cette commande ? »
    /// était sans réponse — ni au moment d'imputer, ni après coup. Or les trois voies
    /// de remboursement peuvent viser la même commande : un retour partiel, puis un
    /// remboursement du paiement entier, retranchaient chacun leur montant sans se
    /// voir. Le plafond posé par <c>ImputationRemboursement</c> se calcule sur cette
    /// colonne.
    /// </summary>
    public Guid OrderId { get; private set; }

    /// <summary>Montant retranché du périmètre PLATEFORME par ce remboursement, après plafonnement.</summary>
    public decimal Amount { get; private set; }

    public DateTime ProcessedAtUtc { get; private set; }

    public static ProcessedRefund Mark(string source, Guid sourceId, Guid orderId, decimal amount)
        => new(source, sourceId, orderId, amount);
}
