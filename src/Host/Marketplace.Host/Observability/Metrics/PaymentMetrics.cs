using System.Diagnostics.Metrics;
using Marketplace.BuildingBlocks.Application.Observability;

namespace Marketplace.Host.Observability.Metrics;

/// <summary>
/// Métriques métier des paiements (meter <see cref="MarketplaceMeters.Payments"/>).
///
/// Règles de sécurité / cardinalité — NE JAMAIS mettre en label : numéro de carte,
/// téléphone, email, nom, identifiant client, secret, contenu de webhook. Seuls des
/// labels à faible cardinalité et non personnels sont autorisés :
/// <c>provider</c>, <c>currency</c>, <c>status</c>, <c>payment_method</c>,
/// <c>failure_reason</c> (ensemble borné).
///
/// Montants : stockés dans la plus petite unité monétaire (centimes EUR, unité XOF…)
/// via un compteur <c>payment_amount_total</c> ventilé par <c>currency</c> — on
/// n'additionne jamais des devises différentes (chaque devise = sa propre série).
/// </summary>
public sealed class PaymentMetrics : IPaymentMetrics, IDisposable
{
    private readonly Meter _meter;

    private readonly Counter<long> _attempts;
    private readonly Counter<long> _success;
    private readonly Counter<long> _failed;
    private readonly Counter<long> _pending;
    private readonly Counter<long> _cancelled;
    private readonly Counter<long> _amountMinor;
    private readonly Counter<long> _refundMinor;
    private readonly Counter<long> _webhookErrors;
    private readonly Histogram<double> _duration;
    private readonly Histogram<double> _webhookDuration;

    public PaymentMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MarketplaceMeters.Payments);

        _attempts = _meter.CreateCounter<long>("payment_attempts", description: "Paiements initiés.");
        _success = _meter.CreateCounter<long>("payment_success", description: "Paiements réussis.");
        _failed = _meter.CreateCounter<long>("payment_failed", description: "Paiements échoués.");
        _pending = _meter.CreateCounter<long>("payment_pending", description: "Paiements passés en attente.");
        _cancelled = _meter.CreateCounter<long>("payment_cancelled", description: "Paiements annulés.");

        _amountMinor = _meter.CreateCounter<long>(
            "payment_amount",
            unit: "{minor_unit}",
            description: "Montant total traité (paiements réussis), en plus petite unité monétaire, par devise.");
        _refundMinor = _meter.CreateCounter<long>(
            "payment_refund",
            unit: "{minor_unit}",
            description: "Montant total remboursé, en plus petite unité monétaire, par devise.");

        _webhookErrors = _meter.CreateCounter<long>("payment_webhook_errors", description: "Erreurs de traitement des webhooks fournisseur.");

        _duration = _meter.CreateHistogram<double>(
            "payment_duration_seconds",
            unit: "s",
            description: "Durée d'un paiement (de l'initiation au statut final).");
        _webhookDuration = _meter.CreateHistogram<double>(
            "payment_webhook_duration_seconds",
            unit: "s",
            description: "Délai de traitement d'un webhook fournisseur.");
    }

    /// <summary>Un paiement est initié.</summary>
    public void Attempt(string provider, string paymentMethod, string currency) =>
        _attempts.Add(1, Tags(provider, paymentMethod, currency));

    /// <summary>
    /// Un paiement a abouti. <paramref name="amountMinorUnits"/> est le montant dans
    /// la plus petite unité (centimes EUR/USD, unité XOF). <paramref name="durationSeconds"/>
    /// est optionnel (durée totale du paiement).
    /// </summary>
    public void Success(string provider, string paymentMethod, string currency, long amountMinorUnits, double? durationSeconds = null)
    {
        var tags = Tags(provider, paymentMethod, currency);
        _success.Add(1, tags);
        if (amountMinorUnits > 0)
        {
            _amountMinor.Add(amountMinorUnits, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("currency", currency));
        }

        if (durationSeconds is > 0)
        {
            _duration.Record(durationSeconds.Value, tags);
        }
    }

    /// <summary>Un paiement a échoué. <paramref name="failureReason"/> doit être borné.</summary>
    public void Failed(string provider, string paymentMethod, string currency, string failureReason)
    {
        _failed.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("payment_method", paymentMethod),
            new KeyValuePair<string, object?>("currency", currency),
            new KeyValuePair<string, object?>("failure_reason", failureReason));
    }

    /// <summary>Un paiement passe en attente (ex. redirection / confirmation asynchrone).</summary>
    public void Pending(string provider, string paymentMethod, string currency) =>
        _pending.Add(1, Tags(provider, paymentMethod, currency));

    /// <summary>Un paiement est annulé (par l'utilisateur ou le système).</summary>
    public void Cancelled(string provider, string paymentMethod, string currency) =>
        _cancelled.Add(1, Tags(provider, paymentMethod, currency));

    /// <summary>Un remboursement est émis (montant en plus petite unité, par devise).</summary>
    public void Refund(string provider, string currency, long amountMinorUnits)
    {
        if (amountMinorUnits <= 0)
        {
            return;
        }

        _refundMinor.Add(amountMinorUnits,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("currency", currency));
    }

    /// <summary>Erreur de traitement d'un webhook fournisseur (signature, parsing, etc.).</summary>
    public void WebhookError(string provider, string failureReason) =>
        _webhookErrors.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("failure_reason", failureReason));

    /// <summary>Délai de traitement d'un webhook (secondes).</summary>
    public void WebhookProcessed(string provider, double durationSeconds) =>
        _webhookDuration.Record(durationSeconds, new KeyValuePair<string, object?>("provider", provider));

    private static KeyValuePair<string, object?>[] Tags(string provider, string paymentMethod, string currency) => new[]
    {
        new KeyValuePair<string, object?>("provider", provider),
        new KeyValuePair<string, object?>("payment_method", paymentMethod),
        new KeyValuePair<string, object?>("currency", currency),
    };

    public void Dispose() => _meter.Dispose();
}
