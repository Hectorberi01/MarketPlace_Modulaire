using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Marketplace.Modules.Ordering.Application.Orders.Commands;

namespace Marketplace.Modules.Ordering.Infrastructure.Maintenance;

/// <summary>
/// Expire périodiquement les commandes restées en attente de paiement, et rend ce
/// qu'elles retenaient : le stock au catalogue, la cagnotte au client.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// Tourne dans l'hôte API (un seul), comme la réconciliation des retraits. En
/// multi-instances, deux balayages pourraient viser la même commande — sans dommage :
/// l'annulation est refusée par le domaine sur une commande déjà annulée, et les deux
/// compensations (stock, cagnotte) portent chacune leur propre verrou de rejeu.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed class AbandonedOrderSweeper : BackgroundService
{
    /// <summary>
    /// Délai avant qu'une commande non payée soit considérée abandonnée.
    ///
    /// SIX HEURES, ET C'EST VOLONTAIREMENT LONG. Un paiement Mobile Money au Bénin peut
    /// mettre de longues minutes à se confirmer, et un client qui ferme l'application
    /// pendant l'opération y revient souvent plus tard dans la journée. Annuler une
    /// commande en train d'aboutir coûterait bien plus cher que de laisser du stock
    /// retenu une demi-journée.
    /// </summary>
    private static readonly TimeSpan AbandonedAfter = TimeSpan.FromHours(6);

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private const int BatchSize = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AbandonedOrderSweeper> _logger;

    public AbandonedOrderSweeper(IServiceScopeFactory scopeFactory, ILogger<AbandonedOrderSweeper> logger)
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

                var result = await sender.Send(
                    new ExpireAbandonedOrdersCommand(AbandonedAfter, BatchSize), stoppingToken);

                if (result.IsSuccess && result.Value > 0)
                {
                    _logger.LogInformation(
                        "Commandes abandonnées : {Count} expirée(s), stock et cagnotte libérés.", result.Value);
                }
            }
            catch (OperationCanceledException)
            {
                break; // arrêt normal de l'application
            }
            catch (Exception ex)
            {
                // On journalise et on continue : un balayage raté n'a pas à emporter
                // l'hôte, et le suivant reprendra les mêmes commandes.
                _logger.LogError(ex, "Échec du balayage des commandes abandonnées.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }
}
