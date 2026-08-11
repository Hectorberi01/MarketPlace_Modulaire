using Marketplace.BuildingBlocks.Domain.Primitives;

namespace HBA.Deliveries.Domain.Deliveries;

/// <summary>Issue d'une proposition faite à un livreur.</summary>
public enum AssignmentOutcome
{
    /// <summary>Proposée, sans réponse pour l'instant.</summary>
    Offered = 0,

    /// <summary>Le livreur a accepté.</summary>
    Accepted = 1,

    /// <summary>Le livreur a refusé.</summary>
    Rejected = 2,

    /// <summary>Le livreur n'a pas répondu dans le délai imparti.</summary>
    Expired = 3,

    /// <summary>L'exploitation a retiré la mission au livreur.</summary>
    Revoked = 4
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE PROPOSITION FAITE À UN LIVREUR — ET SON SORT.
///
/// POURQUOI GARDER L'HISTORIQUE PLUTÔT QU'UN SEUL « DriverId »
///
/// Il serait plus simple de poser un `CurrentDriverId` sur la course et de
/// l'écraser à chaque réaffectation. On perdrait alors ce qui compte vraiment :
///
///   • le nombre de refus avant acceptation — le seul indicateur qui dise si le
///     dispatch propose les bonnes courses aux bons livreurs ;
///   • qui a refusé quoi, et à quelle fréquence : un livreur qui refuse
///     systématiquement les longues distances n'est pas un mauvais livreur, mais
///     il ne faut plus les lui proposer en priorité ;
///   • de quoi répondre à « pourquoi ma commande a mis quarante minutes à
///     partir » autrement que par une supposition.
///
/// Ces lignes sont donc IMMUABLES une fois conclues. On n'écrase pas une
/// proposition, on en ajoute une.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DeliveryAssignment : Entity<Guid>
{
    private DeliveryAssignment(Guid id, DriverId driverId, int attemptNumber)
        : base(id)
    {
        DriverId = driverId;
        AttemptNumber = attemptNumber;
        Outcome = AssignmentOutcome.Offered;
        OfferedAtUtc = DateTime.UtcNow;
    }

    // Requis par EF Core.
    private DeliveryAssignment()
    {
    }

    public DriverId DriverId { get; private set; }

    /// <summary>Rang de la tentative pour cette course : 1 pour la première proposition.</summary>
    public int AttemptNumber { get; private set; }

    public AssignmentOutcome Outcome { get; private set; }

    public DateTime OfferedAtUtc { get; private set; }

    public DateTime? RespondedAtUtc { get; private set; }

    /// <summary>Motif du refus, quand le livreur en donne un.</summary>
    public string? Reason { get; private set; }

    /// <summary>Temps de réponse du livreur. Alimente le score de dispatch.</summary>
    public TimeSpan? ResponseTime => RespondedAtUtc is null ? null : RespondedAtUtc - OfferedAtUtc;

    internal static DeliveryAssignment Offer(DriverId driverId, int attemptNumber)
        => new(Guid.NewGuid(), driverId, attemptNumber);

    internal void Accept()
    {
        Outcome = AssignmentOutcome.Accepted;
        RespondedAtUtc = DateTime.UtcNow;
    }

    internal void Reject(string? reason)
    {
        Outcome = AssignmentOutcome.Rejected;
        RespondedAtUtc = DateTime.UtcNow;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    internal void Expire()
    {
        Outcome = AssignmentOutcome.Expired;
        RespondedAtUtc = DateTime.UtcNow;
    }

    internal void Revoke(string? reason)
    {
        Outcome = AssignmentOutcome.Revoked;
        RespondedAtUtc = DateTime.UtcNow;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}
