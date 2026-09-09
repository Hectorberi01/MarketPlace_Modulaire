namespace Marketplace.Modules.Sellers.Contracts;

/// <summary>
/// Vue COMPLÈTE d'un vendeur — usage INTERNE et PROPRIÉTAIRE/ADMIN uniquement.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// CE RECORD NE DOIT JAMAIS ÊTRE SÉRIALISÉ VERS UN ENDPOINT ANONYME OU PUBLIC.
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
    SellerCompanyInfoSummary? Metadata = null,
    // Motif du dernier refus de KYB. Le vendeur voyait « Rejected » sur son écran de
    // compte sans savoir ce qui n'allait pas, et re-téléversait la pièce refusée.
    string? KybRejectionReason = null,
    // Suspension en cours : sa date et son motif. Null hors suspension. Le compte des
    // suspensions passées, lui, reste dans l'agrégat : c'est un élément de décision
    // d'administration, pas une information à renvoyer au vendeur.
    DateTime? SuspendedOnUtc = null,
    string? SuspensionReason = null)
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
/// <param name="Commune">
/// CODE d'une des 77 communes (« abomey-calavi »). C'est la valeur STOCKÉE, et celle
/// qu'un client renvoie à l'écriture.
/// </param>
/// <param name="CommuneName">
/// Libellé accentué (« Abomey-Calavi »), RÉSOLU PAR LE SERVEUR — jamais stocké.
///
/// Il existe parce que sans lui les écrans en lecture seule affichaient le code brut :
/// la fiche vendeur de la console d'administration et l'écran boutique de l'application
/// vendeur montraient « abomey-calavi » là où le formulaire, juste à côté, affichait
/// « Abomey-Calavi ». Faire résoudre le libellé par chaque client supposerait qu'il ait
/// chargé le référentiel — vrai sur un formulaire, faux sur une simple fiche de lecture.
///
/// Chaîne vide si aucune commune n'est déclarée (le champ reste facultatif : c'est du
/// déclaratif de dossier KYB, pas une adresse de livraison).
/// </param>
public sealed record SellerCompanyInfoSummary(
    string? LegalName,
    string? Rccm,
    string? Ifu,
    string? Address,
    string? Commune,
    string CommuneName,
    string? Activity,
    string? ManagerName,
    string? Phone);
