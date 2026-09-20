using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Marketing.Domain.Content;

/// <summary>Identité forte d'un bloc de contenu.</summary>
public readonly record struct ContentBlockId(Guid Value)
{
    public static ContentBlockId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Type de bloc éditorial.</summary>
public enum ContentBlockType
{
    Banner = 0,
    Page = 1,
    FeaturedList = 2,
    Faq = 3,

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// ANNONCE : UN MESSAGE DATÉ, PAS UNE CRÉATION GRAPHIQUE.
    ///
    /// « Les livraisons sont suspendues jeudi », « le paiement Mobile Money est
    /// indisponible ce soir ». Du texte, un niveau de gravité, éventuellement un lien.
    ///
    /// POURQUOI UN TYPE DE BLOC ET NON UN AGRÉGAT À PART. Une annonce a besoin d'un
    /// statut, d'une fenêtre d'affichage et d'une position — exactement ce que
    /// `ContentBlock` porte déjà, avec sa console, sa publication et son archivage. Elle
    /// n'a besoin d'aucune des trois choses qui ont justifié un agrégat pour le
    /// juridique : pas d'historique de versions, pas de preuve de consentement, pas
    /// d'immuabilité après publication. Refaire `LegalDocument` ici serait payer un
    /// mécanisme pour ce qu'il a d'apparent plutôt que pour ce qu'il résout.
    ///
    /// Le champ `Content` porte un JSON
    /// { niveau, titre, message, lien, libelleLien, fermable } ; le slot porte la
    /// PORTÉE (voir `ContentSlots`). Découper par slot plutôt que par un champ
    /// « portée » évite à chaque surface d'avoir à interpréter une valeur qui ne la
    /// concerne pas : elle lit son emplacement, et ignore les autres.
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// </summary>
    Announcement = 4
}

/// <summary>Statut d'un bloc de contenu.</summary>
public enum ContentStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2
}

/// <summary>
/// Élément éditorial géré sans déploiement (bannière, page, mise en avant, FAQ).
/// Placé dans un slot à une position donnée, avec une fenêtre d'affichage
/// optionnelle. Agrégat racine.
/// </summary>
public sealed class ContentBlock : AggregateRoot<ContentBlockId>
{
    private ContentBlock()
    {
    }

    private ContentBlock(ContentBlockId id, ContentBlockType type, string slot, int position, string content, DateTime? startUtc, DateTime? endUtc)
        : base(id)
    {
        Type = type;
        Slot = slot;
        Position = position;
        Content = content;
        StartUtc = startUtc;
        EndUtc = endUtc;
        Status = ContentStatus.Draft;
    }

    public ContentBlockType Type { get; private set; }
    public string Slot { get; private set; } = default!;
    public int Position { get; private set; }
    public string Content { get; private set; } = default!;
    public DateTime? StartUtc { get; private set; }
    public DateTime? EndUtc { get; private set; }
    public ContentStatus Status { get; private set; }

    public static Result<ContentBlock> Create(ContentBlockType type, string slot, int position, string content, DateTime? startUtc, DateTime? endUtc)
    {
        if (string.IsNullOrWhiteSpace(slot))
        {
            return Error.Validation("marketing.content.slot_required", "L'emplacement (slot) est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return Error.Validation("marketing.content.content_required", "Le contenu est obligatoire.");
        }

        if (startUtc is { } s && endUtc is { } e && e <= s)
        {
            return Error.Validation("marketing.content.period_invalid", "La fenêtre d'affichage est invalide.");
        }

        return new ContentBlock(ContentBlockId.New(), type, slot.Trim(), position, content, startUtc, endUtc);
    }

    public Result Publish()
    {
        Status = ContentStatus.Published;
        return Result.Success();
    }

    public Result Archive()
    {
        Status = ContentStatus.Archived;
        return Result.Success();
    }

    /// <summary>Modifie le bloc (type, slot, position, contenu, fenêtre d'affichage) avec revalidation.</summary>
    public Result Update(ContentBlockType type, string slot, int position, string content, DateTime? startUtc, DateTime? endUtc)
    {
        if (string.IsNullOrWhiteSpace(slot))
        {
            return  Result.Failure( Error.Validation("marketing.content.slot_required", "L'emplacement (slot) est obligatoire."));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return  Result.Failure( Error.Validation("marketing.content.content_required", "Le contenu est obligatoire."));
        }

        if (startUtc is { } s && endUtc is { } e && e <= s)
        {
            return  Result.Failure( Error.Validation("marketing.content.period_invalid", "La fenêtre d'affichage est invalide."));
        }

        Type = type;
        Slot = slot.Trim();
        Position = position;
        Content = content;
        StartUtc = startUtc;
        EndUtc = endUtc;
        return Result.Success();
    }
}
