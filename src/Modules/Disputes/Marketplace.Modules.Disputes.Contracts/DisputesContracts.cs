namespace Marketplace.Modules.Disputes.Contracts;

/// <summary>Message d'un litige.</summary>
public sealed record DisputeMessageSummary(Guid AuthorId, string Body, string? PhotoUrl, DateTime CreatedAtUtc);

/// <summary>Vue publique d'un litige.</summary>
public sealed record DisputeSummary(
    Guid Id,
    Guid OrderId,
    Guid RaisedBy,
    string Type,
    string Status,
    string? Resolution,
    DateTime CreatedAtUtc,
    DateTime? ResolvedAtUtc,
    IReadOnlyList<DisputeMessageSummary> Messages);
