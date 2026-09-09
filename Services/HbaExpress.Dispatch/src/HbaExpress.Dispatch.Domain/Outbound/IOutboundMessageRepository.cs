namespace HbaExpress.Dispatch.Domain.Outbound;

public interface IOutboundMessageRepository
{
    Task AddAsync(OutboundMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Messages prêts à repartir : ni envoyés, ni abandonnés, et dont l'heure du prochain
    /// essai est passée. Les plus anciens d'abord.
    ///
    /// PAS DE « FOR UPDATE SKIP LOCKED » ICI, ET C'EST UN CHOIX — pas un oubli.
    ///
    /// L'outbox du monolithe et le balayage de redispatch, eux, le posent : leurs
    /// doublons coûtent cher (un gain vendeur crédité deux fois, le même coursier appelé
    /// deux fois). Ici, le doublon est ABSORBÉ : la marketplace ne paie qu'une fois par
    /// identifiant de course, c'est écrit dans sa commande et c'est ce qui rend le réessai
    /// du drain possible.
    ///
    /// Ce que coûterait le verrou, en revanche, est réel : il ne tient que jusqu'au
    /// commit, donc il faudrait garder une transaction OUVERTE PENDANT LES APPELS HTTP du
    /// lot. Un lot de cinquante messages vers une marketplace injoignable tiendrait alors
    /// une transaction et une connexion pendant plusieurs minutes, pour protéger de
    /// quelque chose qui ne fait pas de mal.
    ///
    /// On échange donc un envoi en double, sans conséquence, contre l'absence de
    /// transaction longue. Si un jour l'annonce cessait d'être idempotente côté
    /// marketplace, ce raisonnement tomberait — et le verrou redeviendrait obligatoire.
    /// </summary>
    Task<IReadOnlyList<OutboundMessage>> ListPendingAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default);

    /// <summary>Compte des messages abandonnés : ce que la supervision doit surveiller.</summary>
    Task<int> CountDeadLetteredAsync(CancellationToken cancellationToken = default);
}
