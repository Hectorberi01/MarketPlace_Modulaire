namespace Marketplace.Modules.Sellers.Contracts;

/// <summary>
/// Vue COMPLÈTE d'un vendeur — usage INTERNE et PROPRIÉTAIRE/ADMIN uniquement.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// ⚠️ CE RECORD NE DOIT JAMAIS ÊTRE SÉRIALISÉ VERS UN ENDPOINT ANONYME OU PUBLIC.
///
/// Son ancien résumé disait « vue publique ». C'était faux, et dangereusement : il
/// contient le NUMÉRO MOBILE MONEY du vendeur (Payout.AccountNumber), le TAUX DE
/// COMMISSION négocié, et les URLS de ses PIÈCES D'IDENTITÉ (KybDocuments.FileUrl).
///
/// Il était renvoyé tel quel par GET /mobile/shop/{sellerId}, endpoint ANONYME.
/// Un attaquant récoltait les sellerId sur les fiches produit (publiques), puis
/// moissonnait, sans compte, le RIB et les papiers d'identité de tous les vendeurs.
///
/// Pour une vitrine boutique, utilisez <see cref="SellerPublicSummary"/>. Ce record-ci
/// reste réservé aux chemins où l'appelant est le vendeur lui-même ou un administrateur.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed record SellerSummary(
    Guid Id,
    Guid UserId,
    string ShopName,
    string? LogoUrl,
    string? Description,
    string Status,
    string KybStatus,
    decimal CommissionRate,
    decimal Rating,
    int SalesCount,
    PayoutAccountSummary? Payout,
    IReadOnlyList<KybDocumentSummary> KybDocuments,
    // Informations société déclarées (jsonb). Réservé au propriétaire/admin — jamais
    // dans la vitrine publique (voir ToPublic, qui ne l'inclut pas).
    SellerCompanyInfoSummary? Metadata = null)
{
    /// <summary>
    /// Projette la vitrine PUBLIQUE : uniquement ce qu'un visiteur anonyme a le droit
    /// de voir. Ni RIB, ni commission, ni documents KYB.
    /// </summary>
    public SellerPublicSummary ToPublic()
        => new(Id, ShopName, LogoUrl, Description, Rating, SalesCount);
}

/// <summary>
/// Vitrine PUBLIQUE d'une boutique — ce qu'un visiteur anonyme peut voir.
///
/// Contient STRICTEMENT ce qui doit être affiché sur une page boutique. Tout ajout à ce
/// record est une décision de divulgation : réfléchir à deux fois avant d'y mettre quoi
/// que ce soit qui touche à l'identité, l'argent ou les documents du vendeur.
/// </summary>
public sealed record SellerPublicSummary(
    Guid Id,
    string ShopName,
    string? LogoUrl,
    string? Description,
    decimal Rating,
    int SalesCount);

public sealed record KybDocumentSummary(
    Guid Id,
    string Type,
    string FileUrl,
    // Statut par pièce, dérivé de la vérification : Verified si la pièce est
    // vérifiée, Rejected si la boutique est refusée, sinon InReview.
    string Status,
    DateTime UploadedAtUtc,
    DateTime? VerifiedAtUtc);

public sealed record PayoutAccountSummary(
    string Provider,
    string AccountNumber,
    string AccountName);

/// <summary>
/// Informations société déclarées par le vendeur (raison sociale, RCCM, IFU…).
/// Miroir contractuel du VO domaine SellerCompanyInfo. Tous les champs optionnels.
/// </summary>
public sealed record SellerCompanyInfoSummary(
    string? LegalName,
    string? Rccm,
    string? Ifu,
    string? Address,
    string? City,
    string? Activity,
    string? ManagerName,
    string? Phone);
