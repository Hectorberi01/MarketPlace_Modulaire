namespace Marketplace.Modules.Marketing.Contracts;

/// <summary>Vue publique d'une campagne.</summary>
public sealed record CampaignSummary(
    Guid Id, string Name, string Type, DateTime StartUtc, DateTime EndUtc, string? TargetSegment, string Status, IReadOnlyList<Guid> PromotionIds);

/// <summary>Vue publique d'un bloc de contenu.</summary>
public sealed record ContentBlockSummary(
    Guid Id, string Type, string Slot, int Position, string Content, DateTime? StartUtc, DateTime? EndUtc, string Status);
