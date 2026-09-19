using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Marketing.Domain.Legal;

/// <summary>Identité forte d'une version de document légal.</summary>
public readonly record struct LegalDocumentVersionId(Guid Value)
{
    public static LegalDocumentVersionId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Cycle de vie d'une version.</summary>
public enum LegalVersionStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2
}

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════════
/// UNE RÉDACTION DATÉE, QUE PLUS RIEN NE MODIFIE UNE FOIS PUBLIÉE.
///
/// C'est la pièce que le modèle précédent n'avait pas. Le texte vivait dans une colonne
/// unique du bloc de contenu, et chaque correction l'écrasait : la rédaction que des
/// milliers de personnes avaient acceptée disparaissait sans laisser de trace, et plus
/// rien ne permettait de dire ce qu'elles avaient eu sous les yeux.
///
/// Une version PUBLIÉE est donc immuable. Corriger une virgule veut dire créer une
/// version nouvelle — et c'est exactement ce qu'on veut : l'ancienne rédaction reste
/// lisible tant qu'une acceptation la désigne.
/// ═══════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class LegalDocumentVersion : Entity<LegalDocumentVersionId>
{
    // Requis par EF Core.
    private LegalDocumentVersion()
    {
    }

    private LegalDocumentVersion(
        LegalDocumentVersionId id,
        LegalDocumentId documentId,
        LegalVersionNumber number,
        string content,
        DateTime createdOnUtc)
        : base(id)
    {
        DocumentId = documentId;
        Number = number;
        Content = content;
        Status = LegalVersionStatus.Draft;
        CreatedOnUtc = createdOnUtc;
    }

    public LegalDocumentId DocumentId { get; private set; }

    /// <summary>Le numéro de version, sous sa forme « majeur.mineur ».</summary>
    public LegalVersionNumber Number { get; private set; }

    /// <summary>
    /// Le corps du document, sérialisé. Le domaine ne l'interprète pas : sa forme est
    /// validée par la couche applicative AVANT d'arriver ici, au moment de la rédaction
    /// et non au moment de la lecture. Un texte mal formé refusé à l'enregistrement est
    /// un incident ; le même découvert par un acheteur en train d'accepter en est un
    /// autre.
    /// </summary>
    public string Content { get; private set; } = default!;

    public LegalVersionStatus Status { get; private set; }

    /// <summary>
    /// Date à partir de laquelle cette version FAIT FOI. Elle peut être postérieure à la
    /// publication : on prépare et on publie un texte le 1er, applicable le 15, et les
    /// surfaces continuent de servir l'ancienne jusque-là. Sans ce champ, publier
    /// signifierait « immédiatement », et préparer une révision à l'avance serait
    /// impossible.
    /// </summary>
    public DateTime? EffectiveFromUtc { get; private set; }

    public DateTime? PublishedOnUtc { get; private set; }

    /// <summary>Identité de l'administrateur qui a publié. Jamais renseignée par le client.</summary>
    public Guid? PublishedBy { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    internal static Result<LegalDocumentVersion> CreateDraft(
        LegalDocumentId documentId,
        LegalVersionNumber number,
        string content,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Result.Failure<LegalDocumentVersion>(Error.Validation(
                "marketing.legal.content_required",
                "Le contenu de la version est obligatoire."));
        }

        return Result.Success(new LegalDocumentVersion(
            LegalDocumentVersionId.New(), documentId, number, content, nowUtc));
    }

    /// <summary>
    /// Modifie le brouillon. Refusé dès que la version n'est plus un brouillon : c'est
    /// l'immutabilité annoncée plus haut, et le seul endroit où elle se défend.
    /// </summary>
    internal Result EditDraft(LegalVersionNumber number, string content)
    {
        if (Status != LegalVersionStatus.Draft)
        {
            return Result.Failure(Error.Conflict(
                "marketing.legal.version_not_draft",
                "Une version publiée ou archivée ne se modifie plus. Créez une nouvelle version."));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return Result.Failure(Error.Validation(
                "marketing.legal.content_required",
                "Le contenu de la version est obligatoire."));
        }

        Number = number;
        Content = content;
        return Result.Success();
    }

    internal Result Publish(Guid publishedBy, DateTime nowUtc, DateTime? effectiveFromUtc)
    {
        if (Status != LegalVersionStatus.Draft)
        {
            return Result.Failure(Error.Conflict(
                "marketing.legal.version_not_draft",
                "Seul un brouillon peut être publié."));
        }

        Status = LegalVersionStatus.Published;
        PublishedOnUtc = nowUtc;
        PublishedBy = publishedBy;

        // Pas de date d'effet fournie : le texte fait foi tout de suite. C'est le cas
        // courant, et l'écrire explicitement évite un NULL qui voudrait dire deux choses.
        EffectiveFromUtc = effectiveFromUtc ?? nowUtc;
        return Result.Success();
    }

    internal void Archive() => Status = LegalVersionStatus.Archived;
}
