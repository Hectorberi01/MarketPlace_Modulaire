namespace Marketplace.Modules.Disputes.Domain.Disputes;

/// <summary>Identité forte d'un litige.</summary>
public readonly record struct DisputeId(Guid Value)
{
    public static DisputeId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Type de litige.</summary>
public enum DisputeType
{
    NotReceived = 0,
    NotConforming = 1,
    DamagedItem = 2,
    Other = 3
}

/// <summary>Cycle de vie d'un litige : la plateforme arbitre, l'escrow est gelé.</summary>
public enum DisputeStatus
{
    Open = 0,
    UnderReview = 1,
    Resolved = 2,
    Escalated = 3
}

/// <summary>Décision d'arbitrage.</summary>
public enum DisputeResolution
{
    RefundBuyer = 0,
    ReleaseToSeller = 1,
    PartialRefund = 2
}
