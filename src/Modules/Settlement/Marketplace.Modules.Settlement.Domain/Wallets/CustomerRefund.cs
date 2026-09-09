using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Settlement.Domain.Wallets;

/// <summary>
/// Remboursement DIRECT d'un client, initié par l'admin sur une commande, hors du
/// flux « retour ». L'argent part par un payout FedaPay Mobile Money vers le numéro
/// du client (FedaPay ne rembourse pas une transaction via l'API) et le coût est
/// débité du portefeuille plateforme.
///
/// Cycle calqué sur les retraits vendeur : on ne clôture en Completed que sur
/// confirmation du PSP ; sur issue indéterminée, on reste Processing (jamais de
/// contre-passation qui autoriserait un second versement).
/// </summary>
public sealed class CustomerRefund : AggregateRoot<CustomerRefundId>
{
    private CustomerRefund()
    {
    }

    private CustomerRefund(
        CustomerRefundId id, Guid orderId, Guid buyerId, decimal amount, string currency,
        string reason, string msisdn, string provider, CustomerRefundChannel channel)
        : base(id)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Reason = reason;
        Msisdn = msisdn;
        Provider = provider;
        Channel = channel;

        // La jambe CAGNOTTE est terminée dès sa création : aucun prestataire n'a rien à
        // confirmer, l'écriture de portefeuille est faite dans la même transaction. La
        // laisser en « Processing » la ferait entrer dans la file de réconciliation, qui
        // interrogerait FedaPay sur un versement qui n'existe pas.
        Status = channel == CustomerRefundChannel.Wallet
            ? CustomerRefundStatus.Completed
            : CustomerRefundStatus.Processing;

        CreatedAtUtc = DateTime.UtcNow;
        SentToPspAtUtc = channel == CustomerRefundChannel.Cash ? CreatedAtUtc : null;
        CompletedAtUtc = channel == CustomerRefundChannel.Wallet ? CreatedAtUtc : null;
    }

    public Guid OrderId { get; private set; }
    public Guid BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public string Reason { get; private set; } = default!;
    public string Msisdn { get; private set; } = default!;
    public string Provider { get; private set; } = default!;

    /// <summary>Espèces (versement Mobile Money) ou cagnotte. Voir <see cref="CustomerRefundChannel"/>.</summary>
    public CustomerRefundChannel Channel { get; private set; }

    public CustomerRefundStatus Status { get; private set; }
    public string? ProviderRef { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SentToPspAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>En cours de versement au PSP, en attente de confirmation (réconciliation).</summary>
    public bool IsProcessing => Status == CustomerRefundStatus.Processing;

    /// <summary>Jambe ESPÈCES : un versement Mobile Money part vers le numéro du client.</summary>
    public static CustomerRefund CreateCash(
        Guid orderId, Guid buyerId, decimal amount, string currency, string reason, string msisdn, string provider)
        => new(CustomerRefundId.New(), orderId, buyerId, amount,
            NormalizeCurrency(currency), reason.Trim(), msisdn.Trim(), provider.Trim(),
            CustomerRefundChannel.Cash);

    /// <summary>
    /// Jambe CAGNOTTE : rien ne sort, le client est crédité. Ni numéro ni opérateur —
    /// les exiger obligerait à inventer une donnée qu'aucune écriture n'utilisera.
    /// </summary>
    public static CustomerRefund CreateWallet(
        Guid orderId, Guid buyerId, decimal amount, string currency, string reason)
        => new(CustomerRefundId.New(), orderId, buyerId, amount,
            NormalizeCurrency(currency), reason.Trim(), string.Empty, string.Empty,
            CustomerRefundChannel.Wallet);

    private static string NormalizeCurrency(string currency)
        => string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant();

    /// <summary>
    /// Versement DEMANDÉ au PSP (start accepté) ou issue indéterminée : on garde le
    /// débit plateforme, la réconciliation tranchera.
    /// </summary>
    public void MarkProcessing(string? providerRef, string? note = null)
    {
        Status = CustomerRefundStatus.Processing;
        ProviderRef = providerRef ?? ProviderRef;
        FailureReason = note;
        SentToPspAtUtc ??= DateTime.UtcNow;
    }

    /// <summary>Versement CONFIRMÉ par le PSP (statut « sent »).</summary>
    public void Complete(string? providerRef)
    {
        Status = CustomerRefundStatus.Completed;
        ProviderRef = providerRef ?? ProviderRef;
        FailureReason = null;
        CompletedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Rejet définitif du PSP : le débit plateforme est contre-passé par le handler.</summary>
    public void Fail(string reason)
    {
        Status = CustomerRefundStatus.Failed;
        FailureReason = reason;
        CompletedAtUtc = DateTime.UtcNow;
    }
}

/// <summary>Total déjà remboursé (non échoué) pour une commande, via ce flux direct.</summary>
public interface ICustomerRefundRepository
{
    Task AddAsync(CustomerRefund refund, CancellationToken cancellationToken = default);

    Task<CustomerRefund?> GetByIdAsync(CustomerRefundId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Somme des remboursements NON échoués (Processing + Completed) d'une commande,
    /// LES DEUX CANAUX CONFONDUS. C'est le plafond de VALEUR : on ne rembourse jamais
    /// plus que la commande ne vaut, quel que soit le canal.
    /// </summary>
    Task<decimal> SumActiveForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Somme des seules jambes ESPÈCES non échouées. C'est un plafond DIFFÉRENT du
    /// précédent : il borne ce qui peut encore sortir de la caisse, quand l'autre borne
    /// la valeur totale rendue. Les confondre ferait ressortir en espèces une part qui
    /// n'y était jamais entrée.
    /// </summary>
    Task<decimal> SumActiveCashForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remboursements clients encore « en cours » (pour la réconciliation), BORNÉS EN
    /// BASE. Le lot était auparavant tronqué en mémoire après avoir tout matérialisé —
    /// ce qui, sur une file qui ne se vide pas, revient à ne pas le borner du tout.
    /// </summary>
    Task<IReadOnlyList<CustomerRefund>> ListProcessingAsync(int take, CancellationToken cancellationToken = default);
}
