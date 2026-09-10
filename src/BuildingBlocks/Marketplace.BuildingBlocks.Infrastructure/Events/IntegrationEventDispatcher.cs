using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Marketplace.BuildingBlocks.IntegrationEvents;

namespace Marketplace.BuildingBlocks.Infrastructure.Events;

/// <summary>
/// Échec d'un ou plusieurs gestionnaires d'un même événement d'intégration.
///
/// Son <see cref="Exception.Message"/> NOMME les fautifs, parce que c'est la seule
/// chose que l'outbox conserve : <c>OutboxRetryPolicy.RegisterFailure</c> n'enregistre
/// que ce texte dans la colonne <c>Error</c>, et c'est lui qu'un administrateur lira
/// sur l'écran des lettres mortes. Une <c>AggregateException</c> ordinaire y aurait
/// écrit « One or more errors occurred », ce qui n'aide personne.
/// </summary>
public sealed class IntegrationEventHandlingException : Exception
{
    public IntegrationEventHandlingException(string message, IReadOnlyList<Exception> failures)
        : base(message, failures.Count == 1 ? failures[0] : new AggregateException(failures))
        => Failures = failures;

    public IReadOnlyList<Exception> Failures { get; }
}

/// <summary>
/// Dispatch in-process d'un event d'intégration vers tous ses handlers
/// enregistrés. C'est l'implémentation « bus en mémoire » d'aujourd'hui ;
/// demain, le même contrat est rempli par un consumer Kafka — sans toucher au
/// code métier des handlers.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// UN GESTIONNAIRE QUI TOMBE N'EMPORTE PLUS LES SUIVANTS.
///
/// Cette boucle enchaînait les `await` sans rien attraper : la première exception
/// sortait de la méthode, et TOUS les gestionnaires enregistrés après celui-là
/// n'étaient jamais appelés. L'ordre d'exécution étant celui de l'enregistrement des
/// modules, cela revenait à hiérarchiser silencieusement les conséquences d'un même
/// fait métier — les derniers inscrits étant les premiers sacrifiés.
///
/// L'effet observé : `OrderConfirmedIntegrationEvent` a huit consommateurs, dont
/// Analytics EN DERNIER. Un incident dans n'importe lequel des sept autres empêchait
/// l'accumulation du chiffre d'affaires. Rien ne tombait en panne visiblement : la
/// courbe cessait simplement de monter, et se lisait comme une baisse d'activité.
///
/// DEUX PROPRIÉTÉS, ET IL FAUT LES DEUX :
///
///   - CHAQUE gestionnaire est appelé, quoi qu'il arrive aux autres. L'ordre
///     d'enregistrement cesse de décider qui est servi.
///   - L'ÉCHEC REMONTE QUAND MÊME, à la fin. Avaler les exceptions aurait fait
///     « réussir » le message : il serait sorti de l'outbox, et le travail du
///     gestionnaire fautif aurait été perdu sans trace ni rejeu possible. En relevant,
///     le message repasse par les temporisations, puis finit en lettre morte — visible
///     sur `GET /admin/outbox/dead-letters`, et rejouable.
///
/// CE QUE ÇA SUPPOSE DES GESTIONNAIRES, ET QUI ÉTAIT DÉJÀ VRAI : ils doivent être
/// idempotents. L'outbox a toujours été « au moins une fois » — un message rejoué
/// repassait déjà par les gestionnaires qui avaient réussi au tour précédent. Ce
/// changement n'ajoute pas cette exigence, il la rend seulement plus fréquente pour
/// les gestionnaires placés APRÈS un fautif.
///
/// LE DISPATCHER DES DOMAIN EVENTS, LUI, NE DOIT PAS ÊTRE ISOLÉ. Il s'exécute à
/// l'intérieur de `SaveChangesAsync`, avant le commit : un gestionnaire en échec DOIT
/// y interrompre toute la transaction, sinon on committerait un travail à moitié fait.
/// Les deux dispatchers se ressemblent et n'ont pas les mêmes garanties.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class IntegrationEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IntegrationEventDispatcher> _logger;

    /// <param name="logger">
    /// FACULTATIF, et il doit le rester. Ce dispatcher est enregistré par
    /// `AddBuildingBlocksInfrastructure`, que des montages de test composent SANS
    /// `AddLogging()` — en exiger un ferait échouer la résolution pour une trace.
    /// Le repli sur `NullLogger` coûte deux lignes et referme le sujet.
    /// </param>
    public IntegrationEventDispatcher(
        IServiceProvider serviceProvider, ILogger<IntegrationEventDispatcher>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger ?? NullLogger<IntegrationEventDispatcher>.Instance;
    }

    public async Task DispatchAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(integrationEvent.GetType());
        var handlers = _serviceProvider.GetServices(handlerType);
        var method = handlerType.GetMethod(nameof(IIntegrationEventHandler<IntegrationEvent>.HandleAsync))!;
        var nomEvenement = integrationEvent.GetType().Name;

        List<Exception>? echecs = null;
        List<string>? fautifs = null;

        foreach (var handler in handlers)
        {
            if (handler is null)
            {
                continue;
            }

            try
            {
                await (Task)method.Invoke(handler, new object[] { integrationEvent, cancellationToken })!;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Arrêt du service : ce n'est pas un gestionnaire en échec, et continuer
                // la boucle rallongerait l'extinction pour rien. Le message reste non
                // traité et sera repris au démarrage suivant.
                throw;
            }
            catch (Exception ex)
            {
                // Déballage de TargetInvocationException.
                //
                // Un gestionnaire `async Task` ne passe JAMAIS par là : sa machine à
                // états capture tout dans la Task rendue, y compris ce qui est levé
                // avant le premier await, et l'exception ressort telle quelle sur
                // l'await. Le cas visé est l'autre : un gestionnaire non-`async` qui
                // rend une Task et lève en direct — `Invoke` emballe alors, et le motif
                // enregistré dans l'outbox serait « Exception has been thrown by the
                // target of an invocation », qui ne désigne rien.
                var reelle = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
                var nomHandler = handler.GetType().Name;

                _logger.LogError(
                    reelle,
                    "Gestionnaire {Handler} en échec sur {Evenement} ({EvenementId}). Les autres gestionnaires sont tout de même exécutés.",
                    nomHandler, nomEvenement, integrationEvent.Id);

                (echecs ??= new List<Exception>()).Add(reelle);
                (fautifs ??= new List<string>()).Add($"{nomHandler} : {reelle.Message}");
            }
        }

        if (echecs is not null)
        {
            throw new IntegrationEventHandlingException(
                $"{echecs.Count} gestionnaire(s) en échec sur {nomEvenement} — {string.Join(" | ", fautifs!)}",
                echecs);
        }
    }
}
