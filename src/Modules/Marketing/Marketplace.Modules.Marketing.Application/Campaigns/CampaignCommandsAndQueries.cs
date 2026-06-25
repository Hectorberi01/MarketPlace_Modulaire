using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Marketing.Application.Abstractions;
using Marketplace.Modules.Marketing.Contracts;
using Marketplace.Modules.Marketing.Domain.Campaigns;

namespace Marketplace.Modules.Marketing.Application.Campaigns;

/// <summary>Crée une campagne (brouillon).</summary>
public sealed record CreateCampaignCommand(string Name, string Type, DateTime StartUtc, DateTime EndUtc, string? TargetSegment) : ICommand<Guid>;

/// <summary>Rattache une promotion à une campagne.</summary>
public sealed record AddPromotionToCampaignCommand(Guid CampaignId, Guid PromotionId) : ICommand;

/// <summary>Planifie une campagne.</summary>
public sealed record ScheduleCampaignCommand(Guid CampaignId) : ICommand;

/// <summary>Active une campagne.</summary>
public sealed record ActivateCampaignCommand(Guid CampaignId) : ICommand;

/// <summary>Termine une campagne.</summary>
public sealed record EndCampaignCommand(Guid CampaignId) : ICommand;

/// <summary>Récupère une campagne.</summary>
public sealed record GetCampaignQuery(Guid CampaignId) : IQuery<CampaignSummary>;

/// <summary>Liste les campagnes.</summary>
public sealed record ListCampaignsQuery : IQuery<IReadOnlyList<CampaignSummary>>;

public sealed class CreateCampaignCommandValidator : AbstractValidator<CreateCampaignCommand>
{
    public CreateCampaignCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Type).Must(v => v is "FlashSale" or "Event" or "Seasonal").WithMessage("Type de campagne invalide.");
        RuleFor(c => c.EndUtc).GreaterThan(c => c.StartUtc);
    }
}

internal static class CampaignMapper
{
    public static CampaignSummary ToSummary(Campaign c) => new(
        c.Id.Value, c.Name, c.Type.ToString(), c.StartUtc, c.EndUtc, c.TargetSegment, c.Status.ToString(), c.PromotionIds.ToList());
}

internal sealed class CreateCampaignCommandHandler : ICommandHandler<CreateCampaignCommand, Guid>
{
    private readonly ICampaignRepository _repository;
    private readonly IMarketingUnitOfWork _unitOfWork;

    public CreateCampaignCommandHandler(ICampaignRepository repository, IMarketingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateCampaignCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<CampaignType>(command.Type, ignoreCase: true, out var type))
        {
            return Result.Failure<Guid>(Error.Validation("marketing.campaign.type_invalid", "Type de campagne inconnu."));
        }

        var result = Campaign.Create(command.Name, type, command.StartUtc, command.EndUtc, command.TargetSegment);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _repository.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result.Value.Id.Value;
    }
}

internal abstract class CampaignMutationHandlerBase
{
    protected readonly ICampaignRepository Repository;
    protected readonly IMarketingUnitOfWork UnitOfWork;

    protected CampaignMutationHandlerBase(ICampaignRepository repository, IMarketingUnitOfWork unitOfWork)
    {
        Repository = repository;
        UnitOfWork = unitOfWork;
    }

    protected async Task<Result> MutateAsync(Guid id, Func<Campaign, Result> mutate, CancellationToken ct)
    {
        var campaign = await Repository.GetByIdAsync(new CampaignId(id), ct);
        if (campaign is null)
        {
            return Result.Failure(Error.NotFound("marketing.campaign.not_found", "Campagne introuvable."));
        }

        var result = mutate(campaign);
        if (result.IsFailure)
        {
            return result;
        }

        await UnitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class AddPromotionToCampaignCommandHandler : CampaignMutationHandlerBase, ICommandHandler<AddPromotionToCampaignCommand>
{
    public AddPromotionToCampaignCommandHandler(ICampaignRepository r, IMarketingUnitOfWork u) : base(r, u) { }
    public Task<Result> Handle(AddPromotionToCampaignCommand c, CancellationToken ct) => MutateAsync(c.CampaignId, x => x.AddPromotion(c.PromotionId), ct);
}

internal sealed class ScheduleCampaignCommandHandler : CampaignMutationHandlerBase, ICommandHandler<ScheduleCampaignCommand>
{
    public ScheduleCampaignCommandHandler(ICampaignRepository r, IMarketingUnitOfWork u) : base(r, u) { }
    public Task<Result> Handle(ScheduleCampaignCommand c, CancellationToken ct) => MutateAsync(c.CampaignId, x => x.Schedule(), ct);
}

internal sealed class ActivateCampaignCommandHandler : CampaignMutationHandlerBase, ICommandHandler<ActivateCampaignCommand>
{
    public ActivateCampaignCommandHandler(ICampaignRepository r, IMarketingUnitOfWork u) : base(r, u) { }
    public Task<Result> Handle(ActivateCampaignCommand c, CancellationToken ct) => MutateAsync(c.CampaignId, x => x.Activate(), ct);
}

internal sealed class EndCampaignCommandHandler : CampaignMutationHandlerBase, ICommandHandler<EndCampaignCommand>
{
    public EndCampaignCommandHandler(ICampaignRepository r, IMarketingUnitOfWork u) : base(r, u) { }
    public Task<Result> Handle(EndCampaignCommand c, CancellationToken ct) => MutateAsync(c.CampaignId, x => x.End(), ct);
}

internal sealed class GetCampaignQueryHandler : IQueryHandler<GetCampaignQuery, CampaignSummary>
{
    private readonly ICampaignRepository _repository;
    public GetCampaignQueryHandler(ICampaignRepository repository) => _repository = repository;

    public async Task<Result<CampaignSummary>> Handle(GetCampaignQuery query, CancellationToken cancellationToken)
    {
        var campaign = await _repository.GetByIdAsync(new CampaignId(query.CampaignId), cancellationToken);
        return campaign is null
            ? Error.NotFound("marketing.campaign.not_found", "Campagne introuvable.")
            : CampaignMapper.ToSummary(campaign);
    }
}

internal sealed class ListCampaignsQueryHandler : IQueryHandler<ListCampaignsQuery, IReadOnlyList<CampaignSummary>>
{
    private readonly ICampaignRepository _repository;
    public ListCampaignsQueryHandler(ICampaignRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<CampaignSummary>>> Handle(ListCampaignsQuery query, CancellationToken cancellationToken)
    {
        var campaigns = await _repository.ListAsync(cancellationToken);
        IReadOnlyList<CampaignSummary> summaries = campaigns.Select(CampaignMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}
