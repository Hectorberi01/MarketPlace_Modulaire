using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Marketplace.Modules.Settlement.Application.Wallets;

namespace Marketplace.Modules.Settlement.Infrastructure.Reconciliation;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// LE CONTRÔLE COMPTABLE PÉRIODIQUE : LES SOLDES ET LE GRAND LIVRE DISENT-ILS LA
/// MÊME CHOSE ?
///
/// <see cref="ReconcileWalletsQuery"/> faisait déjà tout le travail, et n'avait qu'un
/// seul appelant : une route d'administration. La dérive n'était donc découverte que
/// le jour où un humain ouvrait l'écran. Or une dérive comptable ne se répare pas
/// toute seule et grandit à chaque passage du code fautif : plus elle est détectée
/// tard, moins les écritures nécessaires au diagnostic sont retrouvables.
///
/// CE SERVICE NE RÉPARE RIEN, ET C'EST VOULU.
///
/// Un écart entre un solde et la somme des écritures signifie qu'un chemin de code
/// écrit l'un sans l'autre. « Corriger » le solde effacerait la seule trace du bug
/// tout en le laissant en place. Le service se contente donc d'ALERTER, au niveau
/// Critical, avec de quoi retrouver le compte concerné.
///
/// POURQUOI UNE CADENCE À PART.
///
/// Ce contrôle lit TOUS les portefeuilles et agrège TOUT le grand livre. Deux
/// requêtes seulement, mais deux requêtes qui grossissent avec l'historique — à
/// l'opposé de la réconciliation des retraits, qui traite un lot borné toutes les
/// deux minutes. Les greffer sur le même minuteur ferait payer un balayage complet
/// du grand livre sept cents fois par jour pour détecter une anomalie qui, elle, ne
/// se produit pas à la minute. D'où un service distinct, à l'heure.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class LedgerIntegrityService : BackgroundService
{
    /// <summary>
    /// Une heure. Assez rare pour que le coût soit négligeable, assez fréquent pour
    /// qu'une dérive apparue le matin ne soit pas découverte le lendemain.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>
    /// Délai avant le PREMIER contrôle. Le démarrage de l'hôte est le moment où les
    /// migrations s'appliquent et où les caches se remplissent : lancer un balayage
    /// complet du grand livre à la première seconde de vie du processus ne mesure
    /// rien d'utile et retarde la mise en service.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Nombre d'écarts détaillés dans le journal. Au-delà, ce n'est plus une anomalie
    /// ponctuelle mais une panne systémique : la liste complète n'apporterait rien
    /// qu'une requête sur la route d'administration ne donne mieux.
    /// </summary>
    private const int MaxDriftsLogged = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LedgerIntegrityService> _logger;

    public LedgerIntegrityService(
        IServiceScopeFactory scopeFactory,
        ILogger<LedgerIntegrityService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);

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

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<ISender>();

            var result = await sender.Send(new ReconcileWalletsQuery(), cancellationToken);

            if (result.IsFailure)
            {
                _logger.LogError(
                    "Contrôle du grand livre : la requête a échoué ({Code}). L'invariant reste NON vérifié pour ce cycle.",
                    result.Error.Code);
                return;
            }

            var view = result.Value;

            if (view.IsBalanced)
            {
                // Debug et non Information : ce message tomberait toutes les heures sur
                // chaque environnement sans rien dire qu'on ne sache déjà. C'est le
                // silence qui est la bonne nouvelle.
                _logger.LogDebug(
                    "Contrôle du grand livre : {Accounts} compte(s) sur {Wallets} portefeuille(s), aucun écart.",
                    view.AccountsChecked, view.WalletsChecked);
                return;
            }

            // ─────────────────────────────────────────────────────────────────────
            // CRITICAL, ET PAS WARNING.
            //
            // De l'argent est apparu ou disparu entre deux représentations qui
            // devraient être identiques. Ce n'est ni un incident réseau ni une limite
            // atteinte : c'est un bug de comptabilité, en production, dont la
            // conséquence est chiffrée. Le niveau doit réveiller quelqu'un.
            // ─────────────────────────────────────────────────────────────────────
            _logger.LogCritical(
                "ÉCART COMPTABLE : {DriftCount} compte(s) en dérive sur {Accounts} vérifié(s), " +
                "écart absolu cumulé {TotalDrift}. Les soldes et le grand livre ne concordent plus. " +
                "Aucune correction automatique n'est appliquée : il faut lire les écritures.",
                view.Drifts.Count, view.AccountsChecked, view.TotalAbsoluteDrift);

            foreach (var drift in view.Drifts.Take(MaxDriftsLogged))
            {
                _logger.LogCritical(
                    "ÉCART COMPTABLE — {OwnerType} {OwnerId}, compte {Account} : solde {WalletBalance}, " +
                    "grand livre {LedgerBalance}, écart {Drift}. Portefeuille {WalletId}.",
                    drift.OwnerType, drift.OwnerId, drift.Account,
                    drift.WalletBalance, drift.LedgerBalance, drift.Drift, drift.WalletId);
            }

            if (view.Drifts.Count > MaxDriftsLogged)
            {
                _logger.LogCritical(
                    "ÉCART COMPTABLE — {Remaining} écart(s) supplémentaire(s) non détaillé(s). " +
                    "Voir la route d'administration de réconciliation des portefeuilles.",
                    view.Drifts.Count - MaxDriftsLogged);
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt normal de l'application.
        }
        catch (Exception ex)
        {
            // Une panne de base ne doit jamais tuer le service : on réessaiera à
            // l'heure suivante. Mais on le dit, sans quoi l'absence d'alerte
            // deviendrait indiscernable d'une comptabilité saine.
            _logger.LogError(ex, "Contrôle du grand livre : échec du cycle.");
        }
    }
}
