using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Marketplace.Modules.Marketing.Application.Campaigns;

namespace Marketplace.Modules.Marketing.Infrastructure;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════════
/// FAIT COMMENCER ET FINIR LES CAMPAGNES TOUTES SEULES.
///
/// `Schedule()`, `Activate()` et `End()` existaient depuis le début, et rien ne les
/// appelait : la transition dépendait d'un clic dans la console. Une vente flash prévue
/// pour 8 h commençait donc quand quelqu'un se levait.
///
/// C'EST DEVENU VISIBLE DEPUIS QUE LES ÉVÉNEMENTS SONT EXPOSÉS. Une campagne active dont
/// la fenêtre est close reste affichée sur la vitrine avec un compte à rebours à zéro,
/// et ses promotions, elles, ont bel et bien cessé de s'appliquer : l'acheteur voit une
/// offre qui n'existe plus. C'est le pire des deux mondes, et c'est ce que ce service
/// corrige.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// UNE MINUTE, PAS CINQ.
///
/// `DiscountExpiryService` balaie toutes les cinq minutes, et c'est très bien pour une
/// remise dont personne ne connaît l'heure exacte. Une vente flash, si : elle est
/// annoncée « à 8 h », et un compte à rebours tourne sous les yeux de l'acheteur.
/// Arriver cinq minutes en retard sur ce rendez-vous se voit ; une minute, non.
/// ─────────────────────────────────────────────────────────────────────────────────
///
/// UN SEUL HÔTE. Les cinq hôtes composent ce module : sans le garde de l'installateur,
/// cinq services feraient basculer les mêmes campagnes en même temps. L'opération est
/// idempotente — rejouée, elle ne trouve rien à faire — mais cinq écritures concurrentes
/// sur la même ligne produiraient des conflits de concurrence pour rien.
/// ═══════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CampaignScheduleService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CampaignScheduleService> _logger;

    public CampaignScheduleService(IServiceScopeFactory scopeFactory, ILogger<CampaignScheduleService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();

                var result = await sender.Send(new SweepCampaignsCommand(DateTime.UtcNow), stoppingToken);
                if (result.IsSuccess && result.Value > 0)
                {
                    _logger.LogInformation("Campagnes : {Count} changement(s) d'état.", result.Value);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Un cycle qui échoue ne doit pas arrêter le service : la cause la plus
                // probable est une base momentanément indisponible, et le cycle suivant
                // rattrapera tout ce qui a été manqué — la commande est idempotente.
                _logger.LogError(ex, "Balayage des campagnes : échec du cycle.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
