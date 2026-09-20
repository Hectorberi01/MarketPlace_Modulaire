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

/// <summary>Modifie une campagne existante.</summary>
public sealed record UpdateCampaignCommand(Guid CampaignId, string Name, string Type, DateTime StartUtc, DateTime EndUtc, string? TargetSegment) : ICommand;

/// <summary>
/// Renseigne la présentation publique d'une campagne : adresse, texte, visuel, et le
/// drapeau qui décide de son exposition.
///
/// COMMANDE SÉPARÉE D'`UpdateCampaignCommand`, ET C'EST VOULU. La mise à jour existante
/// remplace nom, type, fenêtre et segment ; y ajouter quatre champs aurait fait qu'un
/// appelant qui ne les connaît pas — la console d'aujourd'hui, un script, un test —
/// effacerait la présentation à chaque retouche du nom. C'est exactement ce qui perdait
/// la fenêtre d'affichage des bannières avant qu'on ne la corrige.
/// </summary>
public sealed record SetCampaignPresentationCommand(
    Guid CampaignId, string? Slug, string? Description, string? HeroImageUrl, bool IsPublic) : ICommand;

/// <summary>Retire une promotion d'une campagne.</summary>
public sealed record RemovePromotionFromCampaignCommand(Guid CampaignId, Guid PromotionId) : ICommand;

/// <summary>Supprime une campagne.</summary>
public sealed record DeleteCampaignCommand(Guid CampaignId) : ICommand;

/// <summary>
/// Fait avancer les campagnes dont la fenêtre l'exige : celles qui sont planifiées et
/// dont l'heure est venue passent en ACTIVE, celles dont la fin est passée en TERMINÉE.
///
/// POURQUOI CETTE COMMANDE EXISTE. `Schedule()`, `Activate()` et `End()` étaient posés à
/// la main depuis la console : une vente flash qui commence à 8 h commençait donc quand
/// quelqu'un se levait, et une opération terminée restait « active » — donc affichée sur
/// la vitrine, depuis que les événements y sont exposés — jusqu'à ce qu'on y pense.
///
/// ELLE EST IDEMPOTENTE. Rejouée, elle ne trouve plus rien à faire : c'est ce qui permet
/// de la lancer toutes les minutes sans précaution, et de la relancer après une panne
/// sans se demander où elle en était.
/// </summary>
public sealed record SweepCampaignsCommand(DateTime AsOfUtc) : ICommand<int>;

/// <summary>Récupère une campagne.</summary>
public sealed record GetCampaignQuery(Guid CampaignId) : IQuery<CampaignSummary>;

/// <summary>Liste les campagnes.</summary>
public sealed record ListCampaignsQuery : IQuery<IReadOnlyList<CampaignSummary>>;

/// <summary>
/// Les campagnes qu'un VISITEUR peut voir à l'instant donné : publiques, actives, dans
/// leur fenêtre, et pourvues d'une adresse. Triées par fin la plus proche — c'est ce qui
/// se termine bientôt qui mérite d'être vu en premier.
///
/// LA RÈGLE DE VISIBILITÉ VIT DANS L'AGRÉGAT (`EstVisiblePubliquement`), pas ici et
/// surtout pas dans le BFF. Deux endroits qui décident séparément si une campagne est
/// visible finissent par ne plus être d'accord, et c'est la vitrine qui tranche — au
/// pire moment.
/// </summary>
public sealed record ListPublicCampaignsQuery(DateTime AsOfUtc) : IQuery<IReadOnlyList<CampaignSummary>>;

/// <summary>Une campagne publique par son adresse. Échoue si elle n'est pas visible.</summary>
public sealed record GetPublicCampaignQuery(string Slug, DateTime AsOfUtc) : IQuery<CampaignSummary>;

public sealed class CreateCampaignCommandValidator : AbstractValidator<CreateCampaignCommand>
{
    public CreateCampaignCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Type).Must(v => v is "FlashSale" or "Event" or "Seasonal").WithMessage("Type de campagne invalide.");
        RuleFor(c => c.EndUtc).GreaterThan(c => c.StartUtc);
    }
}

/// <summary>
/// Les bornes de longueur, dites AVANT la base.
///
/// EF ne valide pas `MaxLength` côté client : un slug de 121 caractères, une description
/// de 2 001 ou une URL de 501 partent jusqu'à PostgreSQL, qui lève une troncature. Ce
/// code SQL-là n'est reconnu par aucun gestionnaire d'exception du projet — la console
/// recevrait donc un 500 pour une faute de saisie. Tous les autres validateurs du module
/// posent leurs bornes ; celui-ci manquait.
/// </summary>
public sealed class SetCampaignPresentationCommandValidator : AbstractValidator<SetCampaignPresentationCommand>
{
    public SetCampaignPresentationCommandValidator()
    {
        RuleFor(c => c.Slug).MaximumLength(120);
        RuleFor(c => c.Description).MaximumLength(2000);
        RuleFor(c => c.HeroImageUrl).MaximumLength(500);
    }
}

internal static class CampaignMapper
{
    public static CampaignSummary ToSummary(Campaign c) => new(
        c.Id.Value, c.Name, c.Type.ToString(), c.StartUtc, c.EndUtc, c.TargetSegment, c.Status.ToString(), c.PromotionIds.ToList(),
        c.Slug, c.Description, c.HeroImageUrl, c.IsPublic);
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

internal sealed class UpdateCampaignCommandHandler : CampaignMutationHandlerBase, ICommandHandler<UpdateCampaignCommand>
{
    public UpdateCampaignCommandHandler(ICampaignRepository r, IMarketingUnitOfWork u) : base(r, u) { }
    public Task<Result> Handle(UpdateCampaignCommand c, CancellationToken ct)
    {
        if (!Enum.TryParse<CampaignType>(c.Type, ignoreCase: true, out var type))
        {
            return Task.FromResult(Result.Failure(Error.Validation("marketing.campaign.type_invalid", "Type de campagne inconnu.")));
        }

        return MutateAsync(c.CampaignId, x => x.Update(c.Name, type, c.StartUtc, c.EndUtc, c.TargetSegment), ct);
    }
}

internal sealed class SweepCampaignsCommandHandler : ICommandHandler<SweepCampaignsCommand, int>
{
    private readonly ICampaignRepository _repository;
    private readonly IMarketingUnitOfWork _unitOfWork;

    public SweepCampaignsCommandHandler(ICampaignRepository repository, IMarketingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> Handle(SweepCampaignsCommand command, CancellationToken cancellationToken)
    {
        // Lecture SUIVIE : on va écrire. `ListAsync` est en `AsNoTracking` parce que ses
        // deux autres appelants sont des lectures pures — ici il faut des entités que le
        // contexte suit, donc on passe par `GetByIdAsync` pour celles qui bougent.
        var campagnes = await _repository.ListAsync(cancellationToken);

        var aBasculer = campagnes
            .Where(c => Transition(c, command.AsOfUtc) is not null)
            .Select(c => (c.Id, Cible: Transition(c, command.AsOfUtc)!.Value))
            .ToList();

        var modifiees = 0;
        foreach (var (id, cible) in aBasculer)
        {
            var campagne = await _repository.GetByIdAsync(id, cancellationToken);
            if (campagne is null)
            {
                continue;
            }

            var resultat = cible == CampaignStatus.Active ? campagne.Activate() : campagne.End();
            if (resultat.IsSuccess)
            {
                modifiees += 1;
            }
        }

        if (modifiees > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(modifiees);
    }

    /// <summary>
    /// L'état où la campagne DEVRAIT être, ou null s'il n'y a rien à faire.
    ///
    /// LA FIN L'EMPORTE SUR LE DÉBUT. Une campagne planifiée dont la fenêtre est
    /// ENTIÈREMENT passée — cela arrive : on la crée, on l'oublie, on la retrouve trois
    /// semaines plus tard — ne doit pas être activée d'abord pour être terminée au
    /// cycle suivant. Elle aurait été publiquement visible pendant ces quelques
    /// minutes, ce qui est exactement ce que ce service existe pour empêcher.
    ///
    /// UN BROUILLON N'EST JAMAIS TOUCHÉ. Tant que personne ne l'a planifié, il n'a pas
    /// été relu : l'activer automatiquement publierait un travail en cours.
    /// </summary>
    private static CampaignStatus? Transition(Campaign campagne, DateTime asOfUtc)
    {
        if (campagne.Status is CampaignStatus.Draft or CampaignStatus.Ended)
        {
            return null;
        }

        if (campagne.EndUtc <= asOfUtc)
        {
            return CampaignStatus.Ended;
        }

        if (campagne.Status == CampaignStatus.Scheduled && campagne.StartUtc <= asOfUtc)
        {
            return CampaignStatus.Active;
        }

        return null;
    }
}

internal sealed class ListPublicCampaignsQueryHandler : IQueryHandler<ListPublicCampaignsQuery, IReadOnlyList<CampaignSummary>>
{
    private readonly ICampaignRepository _repository;

    public ListPublicCampaignsQueryHandler(ICampaignRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<CampaignSummary>>> Handle(ListPublicCampaignsQuery query, CancellationToken cancellationToken)
    {
        var campagnes = await _repository.ListPubliablesAsync(cancellationToken);
        IReadOnlyList<CampaignSummary> visibles = campagnes
            .Where(c => c.EstVisiblePubliquement(query.AsOfUtc))
            .OrderBy(c => c.EndUtc)
            .Select(CampaignMapper.ToSummary)
            .ToList();

        return Result.Success(visibles);
    }
}

internal sealed class GetPublicCampaignQueryHandler : IQueryHandler<GetPublicCampaignQuery, CampaignSummary>
{
    private readonly ICampaignRepository _repository;

    public GetPublicCampaignQueryHandler(ICampaignRepository repository) => _repository = repository;

    public async Task<Result<CampaignSummary>> Handle(GetPublicCampaignQuery query, CancellationToken cancellationToken)
    {
        var slug = query.Slug?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(slug))
        {
            return Result.Failure<CampaignSummary>(Error.NotFound("marketing.campaign.not_found", "Événement introuvable."));
        }

        var campagne = await _repository.GetBySlugAsync(slug, cancellationToken);

        // MÊME RÉPONSE POUR « n'existe pas » ET « pas encore publique ». Distinguer les
        // deux dirait à un visiteur qu'une opération se prépare sous ce nom-là, ce qui
        // est une information commerciale qu'on ne donne pas avant l'heure.
        if (campagne is null || !campagne.EstVisiblePubliquement(query.AsOfUtc))
        {
            return Result.Failure<CampaignSummary>(Error.NotFound("marketing.campaign.not_found", "Événement introuvable."));
        }

        return Result.Success(CampaignMapper.ToSummary(campagne));
    }
}

internal sealed class SetCampaignPresentationCommandHandler : CampaignMutationHandlerBase, ICommandHandler<SetCampaignPresentationCommand>
{
    public SetCampaignPresentationCommandHandler(ICampaignRepository r, IMarketingUnitOfWork u) : base(r, u) { }

    public Task<Result> Handle(SetCampaignPresentationCommand c, CancellationToken ct)
        => MutateAsync(c.CampaignId, x => x.DefinirPresentation(c.Slug, c.Description, c.HeroImageUrl, c.IsPublic), ct);
}

internal sealed class RemovePromotionFromCampaignCommandHandler : CampaignMutationHandlerBase, ICommandHandler<RemovePromotionFromCampaignCommand>
{
    public RemovePromotionFromCampaignCommandHandler(ICampaignRepository r, IMarketingUnitOfWork u) : base(r, u) { }
    public Task<Result> Handle(RemovePromotionFromCampaignCommand c, CancellationToken ct) => MutateAsync(c.CampaignId, x => x.RemovePromotion(c.PromotionId), ct);
}

internal sealed class DeleteCampaignCommandHandler : ICommandHandler<DeleteCampaignCommand>
{
    private readonly ICampaignRepository _repository;
    private readonly IMarketingUnitOfWork _unitOfWork;

    public DeleteCampaignCommandHandler(ICampaignRepository repository, IMarketingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteCampaignCommand command, CancellationToken cancellationToken)
    {
        var campaign = await _repository.GetByIdAsync(new CampaignId(command.CampaignId), cancellationToken);
        if (campaign is null)
        {
            return Result.Failure(Error.NotFound("marketing.campaign.not_found", "Campagne introuvable."));
        }

        _repository.Remove(campaign);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
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
