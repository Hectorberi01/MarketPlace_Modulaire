using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Disputes.Domain.Disputes.Events;

namespace Marketplace.Modules.Disputes.Domain.Disputes;

/// <summary>
/// Litige acheteur ↔ vendeur sur une commande. La plateforme arbitre ; l'escrow
/// est gelé le temps de la décision (cf. dossier). Agrégat racine : possède son
/// fil de messages et preuves.
/// </summary>
public sealed class Dispute : AggregateRoot<DisputeId>
{
    private readonly List<DisputeMessage> _messages = new();

    private Dispute()
    {
    }

    private Dispute(DisputeId id, Guid orderId, Guid raisedBy, DisputeType type)
        : base(id)
    {
        OrderId = orderId;
        RaisedBy = raisedBy;
        Type = type;
        Status = DisputeStatus.Open;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid OrderId { get; private set; }
    public Guid RaisedBy { get; private set; }
    public DisputeType Type { get; private set; }
    public DisputeStatus Status { get; private set; }
    public DisputeResolution? Resolution { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }

    public IReadOnlyCollection<DisputeMessage> Messages => _messages.AsReadOnly();

    public static Result<Dispute> Open(Guid orderId, Guid raisedBy, DisputeType type, string initialMessage, string? photoUrl)
    {
        if (orderId == Guid.Empty || raisedBy == Guid.Empty)
        {
            return Error.Validation("disputes.refs_required", "Commande et auteur sont obligatoires.");
        }

        if (string.IsNullOrWhiteSpace(initialMessage))
        {
            return Error.Validation("disputes.message_required", "Le motif du litige est obligatoire.");
        }

        var dispute = new Dispute(DisputeId.New(), orderId, raisedBy, type);
        dispute._messages.Add(new DisputeMessage(Guid.NewGuid(), raisedBy, initialMessage.Trim(), photoUrl));
        dispute.Raise(new DisputeOpenedDomainEvent(dispute.Id.Value, orderId, raisedBy, type.ToString()));
        return dispute;
    }

    public Result AddMessage(Guid authorId, string body, string? photoUrl)
    {
        if (Status is DisputeStatus.Resolved)
        {
            return Result.Failure(Error.Conflict("disputes.resolved", "Un litige tranché ne reçoit plus de messages."));
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Result.Failure(Error.Validation("disputes.message_required", "Le message ne peut pas être vide."));
        }

        _messages.Add(new DisputeMessage(Guid.NewGuid(), authorId, body.Trim(), photoUrl));
        return Result.Success();
    }

    public Result StartReview()
    {
        if (Status != DisputeStatus.Open)
        {
            return Result.Failure(Error.Conflict("disputes.not_open", "Seul un litige ouvert peut passer en examen."));
        }

        Status = DisputeStatus.UnderReview;
        return Result.Success();
    }

    public Result Resolve(DisputeResolution resolution)
    {
        if (Status is DisputeStatus.Resolved)
        {
            return Result.Failure(Error.Conflict("disputes.already_resolved", "Le litige est déjà tranché."));
        }

        Status = DisputeStatus.Resolved;
        Resolution = resolution;
        ResolvedAtUtc = DateTime.UtcNow;
        Raise(new DisputeResolvedDomainEvent(Id.Value, OrderId, resolution.ToString()));
        return Result.Success();
    }

    public Result Escalate()
    {
        if (Status is DisputeStatus.Resolved)
        {
            return Result.Failure(Error.Conflict("disputes.resolved", "Un litige tranché ne peut pas être escaladé."));
        }

        Status = DisputeStatus.Escalated;
        return Result.Success();
    }

    /// <summary>Rouvre un litige tranché ou escaladé (arbitrage admin) ; repasse en examen.</summary>
    public Result Reopen()
    {
        if (Status is DisputeStatus.Open or DisputeStatus.UnderReview)
        {
            return Result.Failure(Error.Conflict("disputes.not_closed", "Le litige est déjà ouvert."));
        }

        Status = DisputeStatus.UnderReview;
        Resolution = null;
        ResolvedAtUtc = null;
        return Result.Success();
    }
}
