namespace Marketplace.Modules.Returns.Domain.Returns;

/// <summary>Identité forte d'une demande de retour.</summary>
public readonly record struct ReturnRequestId(Guid Value)
{
    public static ReturnRequestId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Motif d'un retour.</summary>
public enum ReturnReason
{
    Defective = 0,
    NotAsDescribed = 1,
    ChangedMind = 2,
    WrongItem = 3,
    Other = 4
}

/// <summary>Cycle de vie d'une demande de retour (RMA).</summary>
public enum ReturnStatus
{
    Requested = 0,
    Approved = 1,
    Rejected = 2,
    Received = 3,
    Refunded = 4
}
