using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.BuildingBlocks.Application.Messaging;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// FAIRE REMONTER L'ÉCHEC D'UN HANDLER D'ÉVÉNEMENT JUSQU'À L'OUTBOX.
///
/// L'outbox ne considère un message comme NON traité que sur EXCEPTION. Un handler
/// qui renvoie un `Result.Failure` — ou pire, qui le jette en rendant un `Task` nu —
/// est donc, de son point de vue, un succès : ni rejeu, ni backoff, ni lettre morte,
/// ni métrique. Tout le mécanisme prévu pour les échecs est contourné.
///
/// Ce helper existe pour que la traduction se fasse au MÊME endroit et de la MÊME
/// façon partout. Un handler qui l'oublie retombe silencieusement dans le défaut ;
/// c'est aussi pourquoi il porte un nom qu'on cherche.
///
/// CE QU'IL NE FAUT PAS EN DÉDUIRE : lever n'est pas la bonne réponse à un rejeu.
/// L'outbox livre AU MOINS UNE FOIS, et un second passage sur un travail déjà fait
/// est normal. C'est au handler de commande de porter sa garde d'idempotence et de
/// renvoyer un SUCCÈS dans ce cas — sinon chaque rejeu légitime finira en alerte.
/// OÙ CE HELPER VIT, ET POURQUOI PAS AILLEURS. Le projet des contrats d'événements
/// est délibérément SANS DÉPENDANCE — un événement d'intégration est un DTO pur qui
/// doit survivre au passage vers un bus externe. Y introduire `Result` casserait cette
/// garantie pour un helper. Il vit donc dans la couche applicative, avec le reste du
/// vocabulaire de commandes.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class IntegrationHandlerFailure
{
    /// <summary>
    /// Lève si le résultat est un échec. Le code d'erreur du domaine est repris dans le
    /// message : c'est lui qu'un opérateur lira dans la lettre morte, et le perdre
    /// obligerait à rejouer le scénario pour comprendre.
    /// </summary>
    public static void ThrowIfFailed(Result result, string context)
    {
        if (result.IsSuccess)
        {
            return;
        }

        throw new IntegrationEventHandlingException(
            $"{context} : ÉCHEC ({result.Error.Code}) — {result.Error.Message}");
    }

    /// <summary>Variante pour un résultat porteur de valeur.</summary>
    public static void ThrowIfFailed<T>(Result<T> result, string context)
    {
        if (result.IsSuccess)
        {
            return;
        }

        throw new IntegrationEventHandlingException(
            $"{context} : ÉCHEC ({result.Error.Code}) — {result.Error.Message}");
    }
}

/// <summary>
/// Échec d'un handler d'événement d'intégration, exprimé sous une forme que l'outbox
/// sait voir. Un type dédié, et non une exception générique : la lettre morte doit
/// distinguer « le handler a refusé » d'une panne d'infrastructure.
/// </summary>
public sealed class IntegrationEventHandlingException : Exception
{
    public IntegrationEventHandlingException(string message) : base(message)
    {
    }
}
