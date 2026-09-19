using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Marketing.Domain.Legal;

/// <summary>Identité forte d'un document légal.</summary>
public readonly record struct LegalDocumentId(Guid Value)
{
    public static LegalDocumentId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Les documents que la plateforme publie.
///
/// LISTE FERMÉE, ET C'EST VOULU : chaque valeur correspond à un écran qui la réclame et
/// à une obligation identifiée. Un type libre laisserait créer « CGU v2 (nouvelle) » à
/// côté de « CGU », sans que rien ne dise laquelle fait foi.
/// </summary>
public enum LegalDocumentKind
{
    /// <summary>Conditions générales d'utilisation.</summary>
    Terms = 0,

    /// <summary>Conditions générales de vente.</summary>
    SalesTerms = 1,

    /// <summary>Conditions applicables aux vendeurs.</summary>
    SellerTerms = 2,

    /// <summary>Politique de confidentialité.</summary>
    Privacy = 3,

    /// <summary>Politique relative aux cookies.</summary>
    Cookies = 4,

    /// <summary>Politique de retour et de remboursement.</summary>
    Refund = 5,

    /// <summary>Politique de livraison.</summary>
    Delivery = 6,

    /// <summary>
    /// Mentions légales.
    ///
    /// Présente pour que la console puisse les servir, mais ce document a une
    /// particularité : RCCM, IFU, siège et directeur de publication ne changent
    /// pratiquement jamais et doivent être exacts. Les tenir dans la configuration du
    /// déploiement reste défendable ; ce qui ne l'est pas, c'est de les avoir aux deux
    /// endroits à la fois.
    /// </summary>
    LegalNotice = 7
}

/// <summary>État du document, tous exemplaires de version confondus.</summary>
public enum LegalDocumentStatus
{
    /// <summary>Créé, jamais publié : aucune surface ne le sert.</summary>
    Draft = 0,

    /// <summary>Au moins une version publiée.</summary>
    Published = 1,

    /// <summary>Retiré : on cesse de le servir sans effacer son historique.</summary>
    Archived = 2
}

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════════
/// UN DOCUMENT LÉGAL ET TOUTES SES RÉDACTIONS SUCCESSIVES.
///
/// L'agrégat porte l'identité stable — le type, l'adresse par laquelle les surfaces le
/// demandent — et la SUITE ORDONNÉE de ses versions. Ce qui change à chaque révision
/// n'est pas le document : c'est une version de plus.
///
/// POURQUOI LES VERSIONS SONT DANS L'AGRÉGAT ET NON À CÔTÉ. Les trois invariants qui
/// comptent ici sont des invariants D'ENSEMBLE : un seul brouillon à la fois, un numéro
/// jamais réemployé, une publication qui ne revient jamais en arrière. Aucun ne peut
/// être défendu par une version qui s'ignorerait l'une l'autre.
/// ═══════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class LegalDocument : AggregateRoot<LegalDocumentId>
{
    private readonly List<LegalDocumentVersion> _versions = new();

    // Requis par EF Core.
    private LegalDocument()
    {
    }

    private LegalDocument(LegalDocumentId id, LegalDocumentKind kind, string slug, string title)
        : base(id)
    {
        Kind = kind;
        Slug = slug;
        Title = title;
        Status = LegalDocumentStatus.Draft;
    }

    public LegalDocumentKind Kind { get; private set; }

    /// <summary>
    /// L'adresse stable du document, celle par laquelle les surfaces le demandent
    /// (« legal_terms_customer »). Elle reprend volontairement les emplacements déjà
    /// employés par les BFF : le modèle change, les appels existants non.
    /// </summary>
    public string Slug { get; private set; } = default!;

    /// <summary>Le titre affiché. Modifiable sans créer de version : ce n'est pas le contrat.</summary>
    public string Title { get; private set; } = default!;

    public LegalDocumentStatus Status { get; private set; }

    /// <summary>
    /// La dernière version publiée. Raccourci de lecture, PAS la source de vérité :
    /// celle-ci reste la collection, et c'est elle que consulte <see cref="EffectiveVersion"/>.
    /// </summary>
    public LegalDocumentVersionId? CurrentVersionId { get; private set; }

    public IReadOnlyCollection<LegalDocumentVersion> Versions => _versions.AsReadOnly();

    public static Result<LegalDocument> Create(LegalDocumentKind kind, string slug, string title)
    {
        var adresse = (slug ?? string.Empty).Trim();
        if (adresse.Length == 0)
        {
            return Result.Failure<LegalDocument>(Error.Validation(
                "marketing.legal.slug_required",
                "L'adresse du document (slug) est obligatoire."));
        }

        var intitule = (title ?? string.Empty).Trim();
        if (intitule.Length == 0)
        {
            return Result.Failure<LegalDocument>(Error.Validation(
                "marketing.legal.title_required",
                "Le titre du document est obligatoire."));
        }

        return Result.Success(new LegalDocument(LegalDocumentId.New(), kind, adresse, intitule));
    }

    public Result Rename(string title)
    {
        var intitule = (title ?? string.Empty).Trim();
        if (intitule.Length == 0)
        {
            return Result.Failure(Error.Validation(
                "marketing.legal.title_required",
                "Le titre du document est obligatoire."));
        }

        Title = intitule;
        return Result.Success();
    }

    /// <summary>Le brouillon en cours, s'il y en a un. Il n'y en a jamais plus d'un.</summary>
    public LegalDocumentVersion? Draft
        => _versions.FirstOrDefault(v => v.Status == LegalVersionStatus.Draft);

    /// <summary>
    /// La version qui FAIT FOI à la date donnée : la plus récemment applicable parmi les
    /// versions publiées.
    ///
    /// Ce n'est pas forcément <see cref="CurrentVersionId"/> : une version publiée le 1er
    /// pour une entrée en vigueur le 15 ne doit pas être servie le 2. C'est précisément
    /// ce qu'un simple pointeur « version courante » ne sait pas exprimer.
    /// </summary>
    public LegalDocumentVersion? EffectiveVersion(DateTime asOfUtc)
        => _versions
            .Where(v => v.Status == LegalVersionStatus.Published
                        && v.EffectiveFromUtc is { } d
                        && d <= asOfUtc)
            .OrderByDescending(v => v.EffectiveFromUtc)
            .FirstOrDefault();

    public LegalDocumentVersion? FindVersion(LegalDocumentVersionId id)
        => _versions.FirstOrDefault(v => v.Id == id);

    /// <summary>
    /// Ouvre — ou réécrit — le brouillon.
    ///
    /// UN SEUL BROUILLON À LA FOIS. Deux brouillons concurrents sur un texte contractuel
    /// donneraient à deux administrateurs l'impression de préparer la même révision, et
    /// l'un des deux travaux serait perdu à la publication sans que personne ne le voie.
    /// « Enregistrer le brouillon » écrit donc toujours au même endroit.
    /// </summary>
    public Result<LegalDocumentVersion> SaveDraft(string versionNumber, string content, DateTime nowUtc)
    {
        if (Status == LegalDocumentStatus.Archived)
        {
            return Result.Failure<LegalDocumentVersion>(Error.Conflict(
                "marketing.legal.document_archived",
                "Ce document est archivé : il n'accepte plus de nouvelle rédaction."));
        }

        var numero = LegalVersionNumber.Parse(versionNumber);
        if (numero.IsFailure)
        {
            return Result.Failure<LegalDocumentVersion>(numero.Error);
        }

        var brouillon = Draft;

        // Le numéro ne doit pas être DÉJÀ EMPLOYÉ par une autre version — le brouillon
        // lui-même excepté, sans quoi le réenregistrer sans changer le numéro échouerait.
        var dejaPris = _versions.Any(v =>
            v.Number == numero.Value && (brouillon is null || v.Id != brouillon.Id));
        if (dejaPris)
        {
            return Result.Failure<LegalDocumentVersion>(Error.Conflict(
                "marketing.legal.version_already_used",
                $"La version {numero.Value} existe déjà pour ce document. Choisissez un numéro supérieur."));
        }

        if (brouillon is not null)
        {
            var edition = brouillon.EditDraft(numero.Value, content);
            return edition.IsFailure
                ? Result.Failure<LegalDocumentVersion>(edition.Error)
                : Result.Success(brouillon);
        }

        var creation = LegalDocumentVersion.CreateDraft(Id, numero.Value, content, nowUtc);
        if (creation.IsFailure)
        {
            return Result.Failure<LegalDocumentVersion>(creation.Error);
        }

        _versions.Add(creation.Value);
        return Result.Success(creation.Value);
    }

    /// <summary>
    /// Publie le brouillon.
    ///
    /// LA PUBLICATION NE REVIENT JAMAIS EN ARRIÈRE : le numéro doit être strictement
    /// supérieur à celui de la dernière version publiée. Publier 1.1 après 2.0 ferait
    /// réapparaître un texte abrogé sous les yeux des utilisateurs, et l'acceptation
    /// enregistrée ensuite désignerait la mauvaise rédaction.
    ///
    /// La version précédente passe en ARCHIVÉE, elle n'est pas supprimée : c'est elle que
    /// désignent les acceptations déjà recueillies.
    /// </summary>
    public Result<LegalDocumentVersion> PublishDraft(Guid publishedBy, DateTime nowUtc, DateTime? effectiveFromUtc)
    {
        if (Status == LegalDocumentStatus.Archived)
        {
            return Result.Failure<LegalDocumentVersion>(Error.Conflict(
                "marketing.legal.document_archived",
                "Ce document est archivé : il ne se publie plus."));
        }

        var brouillon = Draft;
        if (brouillon is null)
        {
            return Result.Failure<LegalDocumentVersion>(Error.NotFound(
                "marketing.legal.no_draft",
                "Aucun brouillon à publier pour ce document."));
        }

        if (publishedBy == Guid.Empty)
        {
            // Le publicateur est une donnée du dossier, pas un ornement : « publié par
            // personne » rendrait la ligne inutilisable le jour où elle sert.
            return Result.Failure<LegalDocumentVersion>(Error.Validation(
                "marketing.legal.publisher_required",
                "L'auteur de la publication est obligatoire."));
        }

        if (effectiveFromUtc is { } debut && debut < nowUtc.Date)
        {
            return Result.Failure<LegalDocumentVersion>(Error.Validation(
                "marketing.legal.effective_in_past",
                "Une date d'entrée en vigueur passée ferait rétroagir un contrat. Choisissez aujourd'hui ou une date future."));
        }

        var publiees = _versions.Where(v => v.Status == LegalVersionStatus.Published).ToList();
        var plusHaute = publiees.Count == 0
            ? (LegalVersionNumber?)null
            : publiees.Max(v => v.Number);

        if (plusHaute is { } precedente && brouillon.Number <= precedente)
        {
            return Result.Failure<LegalDocumentVersion>(Error.Conflict(
                "marketing.legal.version_not_increasing",
                $"La version {brouillon.Number} n'est pas postérieure à la version publiée {precedente}."));
        }

        var publication = brouillon.Publish(publishedBy, nowUtc, effectiveFromUtc);
        if (publication.IsFailure)
        {
            return Result.Failure<LegalDocumentVersion>(publication.Error);
        }

        foreach (var ancienne in publiees)
        {
            ancienne.Archive();
        }

        CurrentVersionId = brouillon.Id;
        Status = LegalDocumentStatus.Published;
        return Result.Success(brouillon);
    }

    /// <summary>Retire le document. L'historique reste, les surfaces cessent de le servir.</summary>
    public Result Archive()
    {
        Status = LegalDocumentStatus.Archived;
        return Result.Success();
    }
}
