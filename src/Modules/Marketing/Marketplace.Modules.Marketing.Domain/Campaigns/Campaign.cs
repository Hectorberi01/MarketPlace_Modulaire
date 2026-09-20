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

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// QUATRE CHAMPS POUR QU'UNE CAMPAGNE PUISSE ÊTRE MONTRÉE.
    ///
    /// L'agrégat savait déjà tout ce qu'il faut pour APPLIQUER une opération — des
    /// promotions, une fenêtre, un statut — et rien de ce qu'il faut pour la
    /// RACONTER. Une campagne n'avait ni adresse publique, ni visuel, ni texte : elle
    /// ne pouvait donc exister que dans la console.
    ///
    /// `IsPublic` EST LE PLUS IMPORTANT DES QUATRE. Toutes les campagnes ne méritent
    /// pas une page : beaucoup ne sont qu'un regroupement interne de promotions, et
    /// les exposer toutes remplirait la vitrine de pages vides portant des noms de
    /// travail. Le défaut est donc PRIVÉ — une campagne devient publique par une
    /// décision, jamais par oubli.
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public string? Slug { get; private set; }

    /// <summary>Texte de présentation affiché sur la page de l'événement.</summary>
    public string? Description { get; private set; }

    /// <summary>Visuel de tête. Même contrat que les bannières : une URL http(s).</summary>
    public string? HeroImageUrl { get; private set; }

    /// <summary>Faux par défaut : une campagne n'est pas exposée sans décision.</summary>
    public bool IsPublic { get; private set; }

    /// <summary>
    /// Une campagne est publiquement AFFICHABLE si elle est publique, active, dans sa
    /// fenêtre, et qu'elle a une adresse.
    ///
    /// LES QUATRE CONDITIONS SONT INDISPENSABLES, et c'est ici qu'elles vivent plutôt
    /// que dans le BFF : « active » est un statut posé à la main, qui ne dit rien de
    /// l'heure qu'il est ; une campagne activée puis oubliée resterait affichée des
    /// mois. Et sans slug, la page n'a pas d'adresse où être servie.
    /// </summary>
    public bool EstVisiblePubliquement(DateTime asOfUtc)
        => IsPublic
           && Status == CampaignStatus.Active
           && !string.IsNullOrWhiteSpace(Slug)
           && StartUtc <= asOfUtc
           && EndUtc > asOfUtc;

    /// <summary>
    /// Renseigne la présentation publique.
    ///
    /// LE SLUG EST NORMALISÉ ET VALIDÉ ICI, pas dans la console : c'est une adresse,
    /// donc une donnée du domaine. Un slug accentué ou espacé donnerait une URL
    /// encodée illisible et impossible à dicter au téléphone.
    /// </summary>
    public Result DefinirPresentation(string? slug, string? description, string? heroImageUrl, bool isPublic)
    {
        var slugNormalise = NormaliserSlug(slug);

        // ── L'ORDRE DES DEUX CONTRÔLES COMPTE ───────────────────────────────────────
        // `NormaliserSlug` rend nul dans DEUX cas : rien de saisi, et saisie
        // inexploitable. Tester d'abord « publique sans adresse » confondait les deux :
        // qui cochait « publique » et tapait « rentrée-2026 » s'entendait répondre
        // qu'il n'avait pas saisi d'adresse, alors qu'il venait de le faire — et le
        // seul message qui dit quoi corriger devenait inatteignable.
        if (!string.IsNullOrWhiteSpace(slug) && slugNormalise is null)
        {
            return Result.Failure(Error.Validation(
                "marketing.campaign.slug_invalid",
                "L'adresse doit contenir des lettres non accentuées, des chiffres et des tirets (ex. « rentree-2026 »)."));
        }

        if (isPublic && slugNormalise is null)
        {
            return Result.Failure(Error.Validation(
                "marketing.campaign.slug_required",
                "Une campagne publique doit avoir une adresse (slug)."));
        }

        // ── LE VISUEL EST SERVI ANONYMEMENT : SON SCHÉMA EST CONTRÔLÉ ICI ───────────
        // Le commentaire de la propriété promettait « une URL http(s) », et rien ne
        // l'imposait : n'importe quelle chaîne était stockée puis renvoyée par
        // `GET /client/events` à trois applications. Une promesse de commentaire n'est
        // pas un contrôle — celui-ci en est un.
        var visuel = string.IsNullOrWhiteSpace(heroImageUrl) ? null : heroImageUrl.Trim();
        if (visuel is not null && !EstUrlHttp(visuel))
        {
            return Result.Failure(Error.Validation(
                "marketing.campaign.hero_image_invalid",
                "Le visuel doit être une adresse complète en http ou https."));
        }

        Slug = slugNormalise;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        HeroImageUrl = visuel;
        IsPublic = isPublic;
        return Result.Success();
    }

    /// <summary>
    /// Rend le slug en minuscules, ou null s'il est vide ou inexploitable.
    ///
    /// AUCUNE TRANSLITTÉRATION : « rentrée » n'est pas transformé en « rentree ». Une
    /// normalisation silencieuse donnerait une adresse que l'administrateur n'a pas
    /// écrite et ne retrouvera pas ; il vaut mieux refuser et le lui dire.
    /// </summary>
    /// <summary>Adresse absolue en http ou https. Tout autre schéma est refusé.</summary>
    private static bool EstUrlHttp(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string? NormaliserSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var brut = slug.Trim().ToLowerInvariant();
        foreach (var c in brut)
        {
            var accepte = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-';
            if (!accepte)
            {
                return null;
            }
        }

        return brut.Trim('-') is { Length: > 0 } propre ? propre : null;
    }

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

    /// <summary>Modifie la campagne (nom, type, fenêtre, segment) avec revalidation.</summary>
    public Result Update(string name, CampaignType type, DateTime startUtc, DateTime endUtc, string? targetSegment)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("marketing.campaign.name_required", "Le nom de la campagne est obligatoire."));
        }

        if (endUtc <= startUtc)
        {
            return Result.Failure(Error.Validation("marketing.campaign.period_invalid", "La fenêtre d'activation est invalide."));
        }

        Name = name.Trim();
        Type = type;
        StartUtc = startUtc;
        EndUtc = endUtc;
        TargetSegment = targetSegment;
        return Result.Success();
    }

    /// <summary>Retire une promotion de la campagne.</summary>
    public Result RemovePromotion(Guid promotionId)
    {
        _promotionIds.Remove(promotionId);
        return Result.Success();
    }
}
