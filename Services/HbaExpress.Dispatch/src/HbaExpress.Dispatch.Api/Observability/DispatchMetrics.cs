using System.Diagnostics.Metrics;
using HbaExpress.Dispatch.Application.Abstractions;

namespace HbaExpress.Dispatch.Api.Observability;

/// <summary>Noms des meters, pour l'enregistrement OpenTelemetry.</summary>
public static class DispatchMeters
{
    public const string Dispatch = "hbaexpress.dispatch";

    public static readonly string[] All = { Dispatch };
}

/// <summary>
/// Les compteurs du service, exposés en OpenTelemetry.
///
/// <c>dispatch_outbound_dead_letters</c> et <c>dispatch_jobs_expired</c> sont les deux qui
/// portent une alerte (voir <c>DispatchOutboundDeadLetter</c> et <c>DispatchJobExpired</c>
/// dans prometheus-rules.yml). Les autres servent aux tableaux de bord — taux
/// d'acceptation, nombre de coursiers sollicités par vague, volume de courses.
/// </summary>
public sealed class DispatchMetrics : IDispatchMetrics, IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _jobsCreated;
    private readonly Counter<long> _offersSent;
    private readonly Counter<long> _jobsAccepted;
    private readonly Counter<long> _jobsDelivered;
    private readonly Counter<long> _jobsFailed;
    private readonly Counter<long> _jobsExpired;
    private readonly Counter<long> _outboundFailures;
    private readonly Counter<long> _outboundDeadLetters;

    public DispatchMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(DispatchMeters.Dispatch);

        _jobsCreated = _meter.CreateCounter<long>(
            "dispatch_jobs_created", description: "Courses créées depuis une commande prête.");

        _offersSent = _meter.CreateCounter<long>(
            "dispatch_offers_sent", description: "Offres poussées à des coursiers.");

        _jobsAccepted = _meter.CreateCounter<long>(
            "dispatch_jobs_accepted", description: "Courses acceptées par un coursier.");

        _jobsDelivered = _meter.CreateCounter<long>(
            "dispatch_jobs_delivered", description: "Courses livrées.");

        _jobsFailed = _meter.CreateCounter<long>(
            "dispatch_jobs_failed",
            description: "Courses déclarées en échec par leur coursier. C'est le TAUX qui compte, pas le zéro.");

        _jobsExpired = _meter.CreateCounter<long>(
            "dispatch_jobs_expired",
            description: "Courses abandonnées faute de coursier. DOIT RESTER À ZÉRO tant que le repli en cascade n'existe pas.");

        _outboundFailures = _meter.CreateCounter<long>(
            "dispatch_outbound_send_failures",
            description: "Envois vers la marketplace en échec (le message sera réessayé).");

        _outboundDeadLetters = _meter.CreateCounter<long>(
            "dispatch_outbound_dead_letters",
            description: "Messages sortants abandonnés. DOIT RESTER À ZÉRO : une course livrée non payée.");
    }

    public void JobCreated() => _jobsCreated.Add(1);

    // Le TOUR est une étiquette, le nombre de coursiers la valeur : cela permet de voir
    // combien de vagues il faut en moyenne pour qu'une course parte, et donc si le rayon
    // initial de trois kilomètres est le bon.
    public void OffersSent(int courierCount, int round)
        => _offersSent.Add(courierCount, new KeyValuePair<string, object?>("round", round));

    public void JobAccepted() => _jobsAccepted.Add(1);

    public void JobDelivered() => _jobsDelivered.Add(1);

    public void JobFailed() => _jobsFailed.Add(1);

    public void JobExpired() => _jobsExpired.Add(1);

    public void OutboundSendFailed(string messageType)
        => _outboundFailures.Add(1, new KeyValuePair<string, object?>("message_type", messageType));

    public void OutboundDeadLettered(string messageType)
        => _outboundDeadLetters.Add(1, new KeyValuePair<string, object?>("message_type", messageType));

    public void Dispose() => _meter.Dispose();
}
