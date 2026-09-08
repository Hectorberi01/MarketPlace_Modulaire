using Marketplace.BuildingBlocks.Domain.Primitives;

namespace HbaExpress.Dispatch.Domain.Outbound;

/// <summary>
/// Un message que le service DOIT faire parvenir à la marketplace.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI UNE FILE PERSISTÉE PLUTÔT QU'UN SIMPLE APPEL HTTP.
///
/// Le message qui compte aujourd'hui est « course livrée » : il déclenche le crédit du
/// coursier. Un POST direct au moment de la livraison paraîtrait suffisant — et perdrait
/// le paiement à la première coupure réseau. La course serait fermée, le coursier
/// libéré, et personne payé, sans erreur ni trace : il ne resterait rien à réessayer.
///
/// Ici, le message est écrit DANS LA MÊME TRANSACTION que la fermeture de la course. Les
/// deux existent ensemble ou aucun des deux. Le drain s'occupe ensuite de le faire
/// partir, autant de fois qu'il le faut — la marketplace étant idempotente sur
/// l'identifiant de course, un doublon ne paie pas deux fois.
///
/// C'est l'outbox du monolithe, en plus petit et sans les parties dont ce service n'a pas
/// besoin. Le principe est le même, et il n'y en a pas d'autre qui tienne : on ne peut pas
/// garantir à la fois un changement d'état et un appel réseau sans les écrire au même
/// endroit.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class OutboundMessage : Entity<Guid>
{
    /// <summary>Course livrée : la marketplace crédite le coursier de la somme annoncée.</summary>
    public const string CourierDeliveryCompletedType = "courier_delivery_completed";

    private OutboundMessage()
    {
    }

    private OutboundMessage(Guid id, string type, string payload, DateTime nowUtc) : base(id)
    {
        Type = type;
        Payload = payload;
        CreatedAtUtc = nowUtc;
        NextAttemptAtUtc = nowUtc;
    }

    public string Type { get; private set; } = default!;

    /// <summary>Le corps JSON, figé À L'ÉCRITURE.</summary>
    /// <remarks>
    /// Sérialisé au moment de l'enregistrement, jamais reconstruit au moment de l'envoi.
    /// Reconstruire relirait l'état courant : un message rejoué trois heures plus tard
    /// annoncerait alors la situation d'aujourd'hui, pas celle qu'il devait annoncer.
    /// </remarks>
    public string Payload { get; private set; } = default!;

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>
    /// Abandonné après trop d'échecs. LE MESSAGE N'EST PAS SUPPRIMÉ : une course livrée
    /// non payée doit rester lisible en base, et une alerte doit se brancher dessus.
    /// </summary>
    public DateTime? DeadLetteredAtUtc { get; private set; }

    public bool IsPending => SentAtUtc is null && DeadLetteredAtUtc is null;

    public static OutboundMessage Create(string type, string payload, DateTime nowUtc)
        => new(Guid.NewGuid(), type, payload, nowUtc);

    public void MarkSent(DateTime nowUtc)
    {
        SentAtUtc = nowUtc;
        LastError = null;
    }

    /// <summary>
    /// Échec d'un envoi : on note, on recule, et on abandonne au bout du plafond.
    ///
    /// DÉLAI CROISSANT, ET NON UN RÉESSAI IMMÉDIAT. Un service en panne ne guérit pas
    /// entre deux tentatives séparées d'une seconde ; marteler ne fait qu'ajouter de la
    /// charge à un système qui en manque déjà, et remplir les journaux au moment où on a
    /// le plus besoin de les lire.
    /// </summary>
    public void MarkFailed(string error, DateTime nowUtc, int maxAttempts, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        AttemptCount++;
        LastError = error.Length <= 1000 ? error : error[..1000];

        if (AttemptCount >= maxAttempts)
        {
            DeadLetteredAtUtc = nowUtc;
            return;
        }

        // 2^(n-1) fois le délai de base, borné. Le calcul se fait en ticks pour éviter
        // qu'un exposant élevé ne déborde.
        var factor = Math.Pow(2, Math.Min(AttemptCount - 1, 16));
        var delayTicks = Math.Min(baseDelay.Ticks * factor, maxDelay.Ticks);
        NextAttemptAtUtc = nowUtc.AddTicks((long)delayTicks);
    }
}
