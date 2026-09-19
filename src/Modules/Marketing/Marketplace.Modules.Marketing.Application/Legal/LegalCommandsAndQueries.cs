using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Marketing.Application.Abstractions;
using Marketplace.Modules.Marketing.Contracts;
using Marketplace.Modules.Marketing.Domain.Legal;

namespace Marketplace.Modules.Marketing.Application.Legal;

// ═════════════════════════════════════════════════════════════════════════════════════
// COMMANDES
// ═════════════════════════════════════════════════════════════════════════════════════

/// <summary>Crée un document légal, sans aucune version.</summary>
public sealed record CreateLegalDocumentCommand(string Kind, string Slug, string Title) : ICommand<Guid>;

/// <summary>Renomme le document. N'affecte AUCUNE version : le titre n'est pas le contrat.</summary>
public sealed record RenameLegalDocumentCommand(Guid DocumentId, string Title) : ICommand;

/// <summary>Écrit ou réécrit le brouillon. Renvoie l'identifiant de la version brouillon.</summary>
public sealed record SaveLegalDraftCommand(
    Guid DocumentId,
    string Version,
    string? LastUpdated,
    string? Company,
    IReadOnlyList<LegalSectionView> Sections) : ICommand<Guid>;

/// <summary>Publie le brouillon. Renvoie l'identifiant de la version publiée.</summary>
public sealed record PublishLegalDraftCommand(
    Guid DocumentId,
    Guid PublishedBy,
    DateTime? EffectiveFromUtc) : ICommand<Guid>;

/// <summary>Retire le document. L'historique reste.</summary>
public sealed record ArchiveLegalDocumentCommand(Guid DocumentId) : ICommand;

/// <summary>
/// Consigne qu'une personne a accepté une version.
///
/// La VERSION est celle que la surface avait sous les yeux, pas « la version courante » :
/// entre l'affichage et le clic, une publication peut avoir eu lieu. Enregistrer la
/// version courante reviendrait à faire signer un document qu'on n'a pas montré.
/// </summary>
public sealed record RecordLegalAcceptanceCommand(
    Guid UserId,
    string Slug,
    string Version,
    string? Context) : ICommand<bool>;

// ═════════════════════════════════════════════════════════════════════════════════════
// REQUÊTES
// ═════════════════════════════════════════════════════════════════════════════════════

/// <summary>Tous les documents et leur historique (console d'administration).</summary>
public sealed record ListLegalDocumentsQuery : IQuery<IReadOnlyList<LegalDocumentSummary>>;

/// <summary>
/// Une version AVEC son texte. <paramref name="VersionId"/> nul : le brouillon s'il y en
/// a un, sinon la version publiée — c'est ce que l'éditeur veut ouvrir par défaut.
/// </summary>
public sealed record GetLegalVersionQuery(Guid DocumentId, Guid? VersionId) : IQuery<LegalVersionDetail>;

/// <summary>Ce qu'une personne a accepté, le plus récent d'abord.</summary>
public sealed record ListUserLegalAcceptancesQuery(Guid UserId) : IQuery<IReadOnlyList<LegalAcceptanceView>>;

// ═════════════════════════════════════════════════════════════════════════════════════
// VALIDATEURS
// ═════════════════════════════════════════════════════════════════════════════════════

public sealed class CreateLegalDocumentCommandValidator : AbstractValidator<CreateLegalDocumentCommand>
{
    public CreateLegalDocumentCommandValidator()
    {
        // Insensible à la casse, COMME LE HANDLER. Deux règles pour la même chose
        // divergent toujours : la console recevait un 400 sur une valeur que le handler
        // aurait acceptée.
        RuleFor(c => c.Kind)
            .Must(v => Enum.TryParse<LegalDocumentKind>(v, ignoreCase: true, out _))
            .WithMessage("Type de document inconnu.");
        RuleFor(c => c.Slug).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Title).NotEmpty().MaximumLength(200);
    }
}

public sealed class SaveLegalDraftCommandValidator : AbstractValidator<SaveLegalDraftCommand>
{
    public SaveLegalDraftCommandValidator()
    {
        RuleFor(c => c.Version).NotEmpty();
        RuleFor(c => c.Sections).NotEmpty().WithMessage("Le document doit comporter au moins une section.");
    }
}

// ═════════════════════════════════════════════════════════════════════════════════════
// HANDLERS — ÉCRITURE
// ═════════════════════════════════════════════════════════════════════════════════════

internal sealed class CreateLegalDocumentCommandHandler : ICommandHandler<CreateLegalDocumentCommand, Guid>
{
    private readonly ILegalDocumentRepository _repository;
    private readonly IMarketingUnitOfWork _unitOfWork;

    public CreateLegalDocumentCommandHandler(ILegalDocumentRepository repository, IMarketingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateLegalDocumentCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<LegalDocumentKind>(command.Kind, ignoreCase: true, out var kind))
        {
            return Result.Failure<Guid>(Error.Validation(
                "marketing.legal.kind_invalid", "Type de document inconnu."));
        }

        var slug = (command.Slug ?? string.Empty).Trim();

        // L'adresse est ce par quoi les surfaces demandent le document. Deux documents
        // sous la même adresse rendraient la réponse dépendante de l'ordre des lignes.
        if (await _repository.SlugExistsAsync(slug, cancellationToken))
        {
            return Result.Failure<Guid>(Error.Conflict(
                "marketing.legal.slug_taken",
                $"L'adresse « {slug} » est déjà utilisée par un autre document."));
        }

        var creation = LegalDocument.Create(kind, slug, command.Title);
        if (creation.IsFailure)
        {
            return Result.Failure<Guid>(creation.Error);
        }

        await _repository.AddAsync(creation.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return creation.Value.Id.Value;
    }
}

internal sealed class RenameLegalDocumentCommandHandler : ICommandHandler<RenameLegalDocumentCommand>
{
    private readonly ILegalDocumentRepository _repository;
    private readonly IMarketingUnitOfWork _unitOfWork;

    public RenameLegalDocumentCommandHandler(ILegalDocumentRepository repository, IMarketingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RenameLegalDocumentCommand command, CancellationToken cancellationToken)
    {
        var document = await _repository.GetByIdAsync(new LegalDocumentId(command.DocumentId), cancellationToken);
        if (document is null)
        {
            return Result.Failure(Error.NotFound("marketing.legal.not_found", "Document légal introuvable."));
        }

        var resultat = document.Rename(command.Title);
        if (resultat.IsFailure)
        {
            return resultat;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class SaveLegalDraftCommandHandler : ICommandHandler<SaveLegalDraftCommand, Guid>
{
    private readonly ILegalDocumentRepository _repository;
    private readonly IMarketingUnitOfWork _unitOfWork;

    public SaveLegalDraftCommandHandler(ILegalDocumentRepository repository, IMarketingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(SaveLegalDraftCommand command, CancellationToken cancellationToken)
    {
        var document = await _repository.GetByIdAsync(new LegalDocumentId(command.DocumentId), cancellationToken);
        if (document is null)
        {
            return Result.Failure<Guid>(Error.NotFound("marketing.legal.not_found", "Document légal introuvable."));
        }

        // VALIDÉ ICI, à l'écriture — pas à la lecture par un acheteur en train d'accepter.
        var charge = new LegalContent.Charge(
            command.LastUpdated,
            command.Company,
            command.Sections
                .Select(s => new LegalContent.ChargeSection(s.Title, s.Body))
                .ToList());

        var serialise = LegalContent.Serialiser(charge);
        if (serialise.IsFailure)
        {
            return Result.Failure<Guid>(serialise.Error);
        }

        var brouillon = document.SaveDraft(command.Version, serialise.Value, DateTime.UtcNow);
        if (brouillon.IsFailure)
        {
            return Result.Failure<Guid>(brouillon.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return brouillon.Value.Id.Value;
    }
}

internal sealed class PublishLegalDraftCommandHandler : ICommandHandler<PublishLegalDraftCommand, Guid>
{
    private readonly ILegalDocumentRepository _repository;
    private readonly IMarketingUnitOfWork _unitOfWork;

    public PublishLegalDraftCommandHandler(ILegalDocumentRepository repository, IMarketingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(PublishLegalDraftCommand command, CancellationToken cancellationToken)
    {
        var document = await _repository.GetByIdAsync(new LegalDocumentId(command.DocumentId), cancellationToken);
        if (document is null)
        {
            return Result.Failure<Guid>(Error.NotFound("marketing.legal.not_found", "Document légal introuvable."));
        }

        var publication = document.PublishDraft(command.PublishedBy, DateTime.UtcNow, command.EffectiveFromUtc);
        if (publication.IsFailure)
        {
            return Result.Failure<Guid>(publication.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return publication.Value.Id.Value;
    }
}

internal sealed class ArchiveLegalDocumentCommandHandler : ICommandHandler<ArchiveLegalDocumentCommand>
{
    private readonly ILegalDocumentRepository _repository;
    private readonly IMarketingUnitOfWork _unitOfWork;

    public ArchiveLegalDocumentCommandHandler(ILegalDocumentRepository repository, IMarketingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ArchiveLegalDocumentCommand command, CancellationToken cancellationToken)
    {
        var document = await _repository.GetByIdAsync(new LegalDocumentId(command.DocumentId), cancellationToken);
        if (document is null)
        {
            return Result.Failure(Error.NotFound("marketing.legal.not_found", "Document légal introuvable."));
        }

        var resultat = document.Archive();
        if (resultat.IsFailure)
        {
            return resultat;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════════
/// ENREGISTRER UNE ACCEPTATION — ET DIRE LA VÉRITÉ QUAND ON NE PEUT PAS.
///
/// Renvoie VRAI si l'acceptation a pu être rattachée à une version publiée du CMS, FAUX
/// si la version présentée à la personne n'y existe pas — cas normal tant que le
/// document n'est pas publié : l'application affiche alors la copie qu'elle embarque.
///
/// On ne fabrique SURTOUT PAS une version fantôme pour faire réussir l'appel. Une ligne
/// d'acceptation qui pointe vers un texte que personne ne peut relire ne vaut rien le
/// jour où on la produit, et vaut pire que rien : elle donne l'illusion d'une preuve.
/// L'appelant reçoit donc un faux, et c'est à la surface de décider — jamais au
/// consentement de la personne d'être bloqué pour cela.
/// ═══════════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class RecordLegalAcceptanceCommandHandler : ICommandHandler<RecordLegalAcceptanceCommand, bool>
{
    private readonly ILegalDocumentRepository _documents;
    private readonly ILegalAcceptanceRepository _acceptances;
    private readonly IMarketingUnitOfWork _unitOfWork;

    public RecordLegalAcceptanceCommandHandler(
        ILegalDocumentRepository documents,
        ILegalAcceptanceRepository acceptances,
        IMarketingUnitOfWork unitOfWork)
    {
        _documents = documents;
        _acceptances = acceptances;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(RecordLegalAcceptanceCommand command, CancellationToken cancellationToken)
    {
        if (command.UserId == Guid.Empty)
        {
            return Result.Failure<bool>(Error.Validation(
                "marketing.legal.acceptance_user_required",
                "L'identifiant de la personne est obligatoire."));
        }

        var document = await _documents.GetBySlugAsync(
            (command.Slug ?? string.Empty).Trim(), cancellationToken);
        if (document is null)
        {
            return Result.Success(false);
        }

        var numero = LegalVersionNumber.Parse(command.Version);
        if (numero.IsFailure)
        {
            // La surface a présenté une version qui n'est pas un numéro : c'est la copie
            // embarquée d'un binaire ancien, dont la « version » était une date.
            return Result.Success(false);
        }

        var version = document.Versions.FirstOrDefault(v =>
            v.Number == numero.Value && v.Status != LegalVersionStatus.Draft);
        if (version is null)
        {
            return Result.Success(false);
        }

        // IDEMPOTENT : rejouer l'appel ne crée pas de seconde ligne et n'écrase pas la
        // date d'origine — laquelle est précisément ce qu'on conserve.
        var existante = await _acceptances.FindAsync(command.UserId, version.Id, cancellationToken);
        if (existante is not null)
        {
            return Result.Success(true);
        }

        var acceptation = LegalAcceptance.Record(
            command.UserId, document.Id, version.Id, DateTime.UtcNow, command.Context);
        if (acceptation.IsFailure)
        {
            return Result.Failure<bool>(acceptation.Error);
        }

        await _acceptances.AddAsync(acceptation.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(true);
    }
}

// ═════════════════════════════════════════════════════════════════════════════════════
// HANDLERS — LECTURE
// ═════════════════════════════════════════════════════════════════════════════════════

internal static class LegalMapper
{
    public static LegalVersionSummary ToSummary(LegalDocumentVersion v) => new(
        v.Id.Value,
        v.Number.ToString(),
        v.Status.ToString(),
        v.EffectiveFromUtc,
        v.PublishedOnUtc,
        v.PublishedBy,
        v.CreatedOnUtc);

    public static LegalDocumentSummary ToSummary(LegalDocument d)
    {
        var courante = d.CurrentVersionId is { } id ? d.FindVersion(id) : null;
        return new LegalDocumentSummary(
            d.Id.Value,
            d.Kind.ToString(),
            d.Slug,
            d.Title,
            d.Status.ToString(),
            d.CurrentVersionId?.Value,
            courante?.Number.ToString(),
            courante?.PublishedOnUtc,
            d.Draft?.Number.ToString(),
            d.Versions
                .OrderByDescending(v => v.CreatedOnUtc)
                .Select(ToSummary)
                .ToList());
    }
}

internal sealed class ListLegalDocumentsQueryHandler
    : IQueryHandler<ListLegalDocumentsQuery, IReadOnlyList<LegalDocumentSummary>>
{
    private readonly ILegalDocumentRepository _repository;

    public ListLegalDocumentsQueryHandler(ILegalDocumentRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<LegalDocumentSummary>>> Handle(
        ListLegalDocumentsQuery query, CancellationToken cancellationToken)
    {
        var documents = await _repository.ListAsync(cancellationToken);
        IReadOnlyList<LegalDocumentSummary> vues = documents.Select(LegalMapper.ToSummary).ToList();
        return Result.Success(vues);
    }
}

internal sealed class GetLegalVersionQueryHandler : IQueryHandler<GetLegalVersionQuery, LegalVersionDetail>
{
    private readonly ILegalDocumentRepository _repository;

    public GetLegalVersionQueryHandler(ILegalDocumentRepository repository) => _repository = repository;

    public async Task<Result<LegalVersionDetail>> Handle(
        GetLegalVersionQuery query, CancellationToken cancellationToken)
    {
        var document = await _repository.GetByIdAsync(new LegalDocumentId(query.DocumentId), cancellationToken);
        if (document is null)
        {
            return Result.Failure<LegalVersionDetail>(Error.NotFound(
                "marketing.legal.not_found", "Document légal introuvable."));
        }

        // Par défaut l'éditeur ouvre ce qui se travaille : le brouillon, et à défaut le
        // texte en vigueur — jamais une version archivée, qu'on ne demande qu'exprès.
        var version = query.VersionId is { } id
            ? document.FindVersion(new LegalDocumentVersionId(id))
            : document.Draft ?? (document.CurrentVersionId is { } c ? document.FindVersion(c) : null);

        if (version is null)
        {
            return Result.Failure<LegalVersionDetail>(Error.NotFound(
                "marketing.legal.version_not_found", "Version introuvable."));
        }

        var charge = LegalContent.Analyser(version.Content);
        if (charge.IsFailure)
        {
            return Result.Failure<LegalVersionDetail>(charge.Error);
        }

        return Result.Success(new LegalVersionDetail(
            document.Id.Value,
            document.Slug,
            document.Title,
            version.Id.Value,
            version.Number.ToString(),
            version.Status.ToString(),
            version.EffectiveFromUtc,
            version.PublishedOnUtc,
            version.PublishedBy,
            LegalContent.EnSections(charge.Value),
            charge.Value.LastUpdated,
            charge.Value.Company));
    }
}

internal sealed class ListUserLegalAcceptancesQueryHandler
    : IQueryHandler<ListUserLegalAcceptancesQuery, IReadOnlyList<LegalAcceptanceView>>
{
    private readonly ILegalAcceptanceRepository _acceptances;
    private readonly ILegalDocumentRepository _documents;

    public ListUserLegalAcceptancesQueryHandler(
        ILegalAcceptanceRepository acceptances, ILegalDocumentRepository documents)
    {
        _acceptances = acceptances;
        _documents = documents;
    }

    public async Task<Result<IReadOnlyList<LegalAcceptanceView>>> Handle(
        ListUserLegalAcceptancesQuery query, CancellationToken cancellationToken)
    {
        var lignes = await _acceptances.ListByUserAsync(query.UserId, cancellationToken);
        if (lignes.Count == 0)
        {
            return Result.Success((IReadOnlyList<LegalAcceptanceView>)Array.Empty<LegalAcceptanceView>());
        }

        // Une seule lecture des documents plutôt qu'une par ligne : il y en a sept, et
        // une personne peut en avoir accepté plusieurs versions.
        var documents = (await _documents.ListAsync(cancellationToken))
            .ToDictionary(d => d.Id);

        var vues = new List<LegalAcceptanceView>(lignes.Count);
        foreach (var ligne in lignes)
        {
            documents.TryGetValue(ligne.DocumentId, out var document);
            var version = document?.FindVersion(ligne.VersionId);

            vues.Add(new LegalAcceptanceView(
                ligne.UserId,
                ligne.DocumentId.Value,
                document?.Slug ?? string.Empty,
                document?.Title ?? string.Empty,
                ligne.VersionId.Value,
                version?.Number.ToString() ?? string.Empty,
                ligne.AcceptedOnUtc,
                ligne.Context));
        }

        return Result.Success((IReadOnlyList<LegalAcceptanceView>)vues);
    }
}
