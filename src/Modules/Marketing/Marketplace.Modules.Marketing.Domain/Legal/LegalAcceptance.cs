using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Marketing.Domain.Legal;

/// <summary>Identité forte d'une acceptation.</summary>
public readonly record struct LegalAcceptanceId(Guid Value)
{
    public static LegalAcceptanceId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════════
/// QUI A ACCEPTÉ QUOI, QUAND, ET DEPUIS OÙ.
///
/// Ce qui existait : DEUX COLONNES SUR LE COMPTE, `AcceptedTermsVersion` et
/// `AcceptedTermsOnUtc`. Une seule case pour toute la plateforme. Un vendeur qui accepte
/// les CGU puis les conditions vendeurs écrasait la trace de la première : la case ne
/// disait plus qu'une chose, et pas celle qu'on chercherait.
///
/// Une ligne par couple (personne, version) rend la question posable : « qu'est-ce que
/// cette personne avait sous les yeux le jour où elle a coché ? ». C'est la seule forme
/// qui y répond, et c'est la pièce qu'on produit le jour d'un litige.
///
/// PAS DE CLÉ ÉTRANGÈRE VERS `identity.users`, et ce n'est pas un oubli : la règle du
/// dépôt est qu'un module ne lit que son propre schéma. `UserId` est donc un identifiant
/// nu. Le prix à payer est qu'une suppression de compte n'efface pas ces lignes
/// automatiquement — ce qui, pour une preuve de consentement, est plutôt l'effet voulu :
/// elles sont anonymisées par le parcours de suppression, jamais perdues avec le compte.
/// ═══════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class LegalAcceptance : AggregateRoot<LegalAcceptanceId>
{
    // Requis par EF Core.
    private LegalAcceptance()
    {
    }

    private LegalAcceptance(
        LegalAcceptanceId id,
        Guid userId,
        LegalDocumentId documentId,
        LegalDocumentVersionId versionId,
        DateTime acceptedOnUtc,
        string context)
        : base(id)
    {
        UserId = userId;
        DocumentId = documentId;
        VersionId = versionId;
        AcceptedOnUtc = acceptedOnUtc;
        Context = context;
    }

    public Guid UserId { get; private set; }

    public LegalDocumentId DocumentId { get; private set; }

    /// <summary>
    /// LA VERSION, et pas seulement le document. C'est tout l'objet de cette table :
    /// « a accepté les CGU » ne veut rien dire, « a accepté les CGU 2.1 » se vérifie.
    /// </summary>
    public LegalDocumentVersionId VersionId { get; private set; }

    public DateTime AcceptedOnUtc { get; private set; }

    /// <summary>
    /// Le geste qui a produit l'acceptation : « signup_seller », « consent_gate_mobile »,
    /// « checkout ». Court et normalisé, jamais une phrase — c'est un champ de filtre,
    /// pas un journal.
    /// </summary>
    public string Context { get; private set; } = default!;

    public static Result<LegalAcceptance> Record(
        Guid userId,
        LegalDocumentId documentId,
        LegalDocumentVersionId versionId,
        DateTime acceptedOnUtc,
        string? context)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<LegalAcceptance>(Error.Validation(
                "marketing.legal.acceptance_user_required",
                "L'identifiant de la personne est obligatoire."));
        }

        if (versionId.Value == Guid.Empty)
        {
            return Result.Failure<LegalAcceptance>(Error.Validation(
                "marketing.legal.acceptance_version_required",
                "La version acceptée est obligatoire."));
        }

        // Un contexte vide est toléré et normalisé : mieux vaut une acceptation dont on
        // ignore le parcours qu'un refus qui empêcherait quelqu'un de s'inscrire.
        var geste = (context ?? string.Empty).Trim();
        if (geste.Length == 0)
        {
            geste = "unspecified";
        }

        if (geste.Length > 60)
        {
            geste = geste[..60];
        }

        return Result.Success(new LegalAcceptance(
            LegalAcceptanceId.New(), userId, documentId, versionId, acceptedOnUtc, geste));
    }
}
