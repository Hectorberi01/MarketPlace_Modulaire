using System.Diagnostics.Metrics;
using Marketplace.BuildingBlocks.Application.Observability;

namespace Marketplace.Host.Observability.Metrics;

/// <summary>
/// Métriques de l'outbox.
///
/// <c>outbox_dead_letters_total</c> est la métrique qui compte vraiment. Elle doit rester
/// à ZÉRO. Toute incrémentation signifie qu'un fait métier ne se produira jamais — un
/// e-mail jamais envoyé, un gain vendeur jamais crédité — et déclenche une alerte
/// (voir <c>OutboxDeadLetter</c> dans prometheus-rules.yml).
///
/// <para>
/// Des COMPTEURS, pas des jauges : une jauge exigerait d'interroger périodiquement 25 tables
/// pour compter les lettres mortes, et raterait celles qu'un administrateur aurait rejouées
/// entre deux relevés. Un compteur enregistre l'ÉVÉNEMENT au moment où il se produit — il ne
/// peut pas être manqué.
/// </para>
/// </summary>
public sealed class OutboxMetrics : IOutboxMetrics, IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _publishFailures;
    private readonly Counter<long> _deadLetters;

    public OutboxMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MarketplaceMeters.Outbox);

        _publishFailures = _meter.CreateCounter<long>(
            "outbox_publish_failures",
            description: "Tentatives de publication échouées (le message sera réessayé).");

        _deadLetters = _meter.CreateCounter<long>(
            "outbox_dead_letters",
            description: "Messages abandonnés après épuisement des tentatives. DOIT RESTER À ZÉRO.");
    }

    public void PublishFailed(string module, string eventType)
        => _publishFailures.Add(1,
            new KeyValuePair<string, object?>("module", module),
            new KeyValuePair<string, object?>("event_type", eventType));

    public void DeadLettered(string module, string eventType)
        => _deadLetters.Add(1,
            new KeyValuePair<string, object?>("module", module),
            new KeyValuePair<string, object?>("event_type", eventType));

    public void Dispose() => _meter.Dispose();
}
