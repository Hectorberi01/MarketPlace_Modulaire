namespace Marketplace.BuildingBlocks.Infrastructure.Hosting;

/// <summary>
/// Cet hôte doit-il exécuter les TÂCHES DE FOND périodiques ?
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LE PIÈGE, ET IL A DÉJÀ COÛTÉ UNE FOIS.
///
/// Tous les modules sont composés IN-PROCESSUS par cinq hôtes : l'API et les quatre BFF.
/// Un `AddHostedService` posé dans l'installeur d'un module démarre donc CINQ fois, dans
/// cinq processus qui ne savent rien les uns des autres.
///
/// L'outbox l'a appris à ses dépens : cinq processeurs lisaient les mêmes messages et les
/// dispatchaient cinq fois, faute de verrou de ligne. D'où <c>OUTBOX_ENABLED=false</c> sur
/// les BFF. Mais ce garde-fou ne protégeait QUE l'outbox — la réconciliation des retraits,
/// elle, interrogeait FedaPay depuis cinq processus, et son commentaire affirmait
/// tranquillement « tourne dans l'hôte API (un seul) » sans que rien ne l'impose.
///
/// Une tâche de fond n'a pas à connaître cette histoire : elle demande ici si elle doit
/// tourner, et la réponse est la même pour toutes.
///
/// LE SIGNAL EST CELUI QUI EXISTE DÉJÀ. On lit <c>BACKGROUND_JOBS_ENABLED</c> s'il est
/// posé, sinon <c>OUTBOX_ENABLED</c> — que les BFF positionnent déjà à « false » dans le
/// compose de production. Aucune configuration à changer pour que ce soit correct dès
/// aujourd'hui, et la variable dédiée reste disponible le jour où l'on voudra séparer les
/// deux.
///
/// CES BALAYAGES-CI NE SONT TOUJOURS PAS RÉPLICABLES, CONTRAIREMENT À L'OUTBOX.
/// L'outbox a reçu son verrou de ligne (SELECT … FOR UPDATE SKIP LOCKED) et supporte
/// désormais plusieurs instances ; l'expiration des remises et la réconciliation des
/// retraits, elles, lisent encore sans réserver. Deux répliques les exécuteraient toutes
/// les deux. D'où cet interrupteur, qui reste nécessaire — à lever balayage par balayage,
/// avec le même verrou, le jour où on les répliquera.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class BackgroundJobs
{
    public static bool EnabledOnThisHost
    {
        get
        {
            var dedicated = Environment.GetEnvironmentVariable("BACKGROUND_JOBS_ENABLED");
            if (!string.IsNullOrWhiteSpace(dedicated))
            {
                return !string.Equals(dedicated, "false", StringComparison.OrdinalIgnoreCase);
            }

            return !string.Equals(
                Environment.GetEnvironmentVariable("OUTBOX_ENABLED"), "false", StringComparison.OrdinalIgnoreCase);
        }
    }
}
