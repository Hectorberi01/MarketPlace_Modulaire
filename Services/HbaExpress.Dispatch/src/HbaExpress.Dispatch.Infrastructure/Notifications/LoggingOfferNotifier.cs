using HbaExpress.Dispatch.Application.Abstractions;
using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;
using Microsoft.Extensions.Logging;

namespace HbaExpress.Dispatch.Infrastructure.Notifications;

/// <summary>
/// Implémentation de repli du port d'offres : elle JOURNALISE au lieu de pousser.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE N'EST PAS UN BOUCHON VIDE, ET LA DIFFÉRENCE COMPTE.
///
/// Le hub temps réel n'existe pas encore. Enregistrer un port sans implémentation ferait
/// échouer le démarrage ; en enregistrer un qui ne fait RIEN ferait pire — le dispatch
/// paraîtrait fonctionner alors qu'aucun téléphone ne sonne, et on chercherait la panne
/// du mauvais côté.
///
/// Ici, chaque offre laisse une trace nommant le coursier, la course et l'échéance. Le
/// service reste utilisable de bout en bout : l'offre existe en base, et le coursier
/// l'accepte par appel HTTP après l'avoir lue sur GET /api/courier/offers. Le temps réel
/// accélère la boucle, il ne la conditionne pas.
///
/// À REMPLACER par l'implémentation SignalR, sans toucher au reste : c'est tout ce que
/// le port est là pour garantir.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class LoggingOfferNotifier : IOfferNotifier
{
    private readonly ILogger<LoggingOfferNotifier> _logger;

    public LoggingOfferNotifier(ILogger<LoggingOfferNotifier> logger) => _logger = logger;

    public Task NotifyOfferAsync(
        CourierId courierId, DeliveryJob job, DateTime expiresAtUtc, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "OFFRE (non poussée, hub absent) — course {JobId}, commande {OrderId}, coursier {CourierId}, " +
            "valable jusqu'à {ExpiresAtUtc:O}.",
            job.Id, job.OrderId, courierId, expiresAtUtc);

        return Task.CompletedTask;
    }

    public Task NotifyOfferWithdrawnAsync(
        CourierId courierId, DeliveryJobId jobId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "RETRAIT D'OFFRE (non poussé, hub absent) — course {JobId}, coursier {CourierId}.",
            jobId, courierId);

        return Task.CompletedTask;
    }
}
