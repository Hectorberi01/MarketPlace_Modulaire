namespace HbaExpress.Dispatch.Domain.Outbound;

public interface IOutboundMessageRepository
{
    Task AddAsync(OutboundMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Messages prêts à repartir : ni envoyés, ni abandonnés, et dont l'heure du prochain
    /// essai est passée. Les plus anciens d'abord.
    ///
    /// PAS DE « FOR UPDATE SKIP LOCKED » — la même dette que l'outbox du monolithe. Deux
    /// drains concurrents liraient les mêmes messages et enverraient chacun le sien. Le
    /// doublon ne coûte rien ici (la marketplace est idempotente sur l'identifiant de
    /// course), mais la règle reste : UN SEUL exécutant tant que ce verrou manque.
    /// </summary>
    Task<IReadOnlyList<OutboundMessage>> ListPendingAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default);

    /// <summary>Compte des messages abandonnés : ce que la supervision doit surveiller.</summary>
    Task<int> CountDeadLetteredAsync(CancellationToken cancellationToken = default);
}
