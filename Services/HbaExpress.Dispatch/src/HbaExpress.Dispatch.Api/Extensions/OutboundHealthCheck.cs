using HbaExpress.Dispatch.Domain.Outbound;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HbaExpress.Dispatch.Api.Extensions;

/// <summary>
/// Signale les messages sortants ABANDONNÉS.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UN ABANDON, C'EST DE L'ARGENT DÛ QUI N'EST PAS PARTI.
///
/// Le drain journalise chaque abandon en erreur — encore faut-il que quelqu'un lise les
/// journaux. Cette sonde rend le compteur interrogeable : la supervision voit « dégradé »
/// et le corps de la réponse dit combien.
///
/// DÉGRADÉ, ET NON EN PANNE, DÉLIBÉRÉMENT. Le service continue de dispatcher des courses
/// parfaitement : rien ne justifie de le sortir du service, et un orchestrateur qui le
/// redémarrerait n'y changerait rien — les messages abandonnés le resteraient. Ce qu'il
/// faut, c'est qu'un humain regarde.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class OutboundHealthCheck : IHealthCheck
{
    private readonly IOutboundMessageRepository _messages;

    public OutboundHealthCheck(IOutboundMessageRepository messages) => _messages = messages;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var deadLettered = await _messages.CountDeadLetteredAsync(cancellationToken);

        return deadLettered == 0
            ? HealthCheckResult.Healthy("Aucun message sortant abandonné.")
            : HealthCheckResult.Degraded(
                $"{deadLettered} message(s) sortant(s) ABANDONNÉ(S) : des courses livrées n'ont pas été " +
                "payées. À traiter à la main — voir les journaux du drain sortant.");
    }
}
