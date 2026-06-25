using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Marketing.Domain.Campaigns;

/// <summary>Identité forte d'une campagne.</summary>
public readonly record struct CampaignId(Guid Value)
{
    public static CampaignId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Type de campagne.</summary>
public enum CampaignType
{
    FlashSale = 0,
    Event = 1,
    Seasonal = 2
}

/// <summary>Statut d'une campagne.</summary>
public enum CampaignStatus
{
    Draft = 0,
    Scheduled = 1,
    Active = 2,
    Ended = 3
}

/// <summary>
/// Campagne promotionnelle transverse (vente flash, événement saisonnier). Lie
/// des promotions du module Pricing et s'appuie sur Notifications pour la
/// diffusion. Agrégat racine.
/// </summary>
public sealed class Campaign : AggregateRoot<CampaignId>
{
    private List<Guid> _promotionIds = new();

    private Campaign()
    {
    }

    private Campaign(CampaignId id, string name, CampaignType type, DateTime startUtc, DateTime endUtc, string? targetSegment)
        : base(id)
    {
        Name = name;
        Type = type;
        StartUtc = startUtc;
        EndUtc = endUtc;
        TargetSegment = targetSegment;
        Status = CampaignStatus.Draft;
    }

    public string Name { get; private set; } = default!;
    public CampaignType Type { get; private set; }
    public DateTime StartUtc { get; private set; }
    public DateTime EndUtc { get; private set; }
    public string? TargetSegment { get; private set; }
    public CampaignStatus Status { get; private set; }
    public IReadOnlyCollection<Guid> PromotionIds => _promotionIds.AsReadOnly();

    public static Result<Campaign> Create(string name, CampaignType type, DateTime startUtc, DateTime endUtc, string? targetSegment)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("marketing.campaign.name_required", "Le nom de la campagne est obligatoire.");
        }

        if (endUtc <= startUtc)
        {
            return Error.Validation("marketing.campaign.period_invalid", "La fenêtre d'activation est invalide.");
        }

        return new Campaign(CampaignId.New(), name.Trim(), type, startUtc, endUtc, targetSegment);
    }

    public Result AddPromotion(Guid promotionId)
    {
        if (Status is CampaignStatus.Ended)
        {
            return Result.Failure(Error.Conflict("marketing.campaign.ended", "Campagne terminée."));
        }

        if (promotionId != Guid.Empty && !_promotionIds.Contains(promotionId))
        {
            _promotionIds.Add(promotionId);
        }

        return Result.Success();
    }

    public Result Schedule()
    {
        if (Status != CampaignStatus.Draft)
        {
            return Result.Failure(Error.Conflict("marketing.campaign.not_draft", "Seule une campagne brouillon peut être planifiée."));
        }

        Status = CampaignStatus.Scheduled;
        return Result.Success();
    }

    public Result Activate()
    {
        if (Status is CampaignStatus.Ended)
        {
            return Result.Failure(Error.Conflict("marketing.campaign.ended", "Campagne terminée."));
        }

        Status = CampaignStatus.Active;
        return Result.Success();
    }

    public Result End()
    {
        Status = CampaignStatus.Ended;
        return Result.Success();
    }
}
