using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Application.Sellers;

/// <summary>Projection de l'agrégat Seller vers le DTO public.</summary>
internal static class SellerMapper
{
    public static SellerSummary ToSummary(Seller seller) => new(
        seller.Id.Value,
        seller.UserId,
        seller.ShopName,
        seller.LogoUrl,
        seller.Description,
        seller.Status.ToString(),
        seller.KybStatus.ToString(),
        seller.CommissionRate,
        seller.Rating,
        seller.SalesCount,
        seller.PayoutAccount is null
            ? null
            : new PayoutAccountSummary(
                seller.PayoutAccount.Provider.ToString(),
                seller.PayoutAccount.AccountNumber,
                seller.PayoutAccount.AccountName),
        seller.KybDocuments
            .Select(d => new KybDocumentSummary(
                d.Id,
                d.Type.ToString(),
                d.FileUrl,
                ResolveDocStatus(d, seller.KybStatus),
                d.UploadedOnUtc,
                d.VerifiedAtUtc))
            .ToList(),
        MapMetadata(seller.Metadata));

    private static SellerCompanyInfoSummary? MapMetadata(SellerCompanyInfo? m) =>
        m is null
            ? null
            : new SellerCompanyInfoSummary(
                m.LegalName, m.Rccm, m.Ifu, m.Address, m.City, m.Activity, m.ManagerName, m.Phone);

    /// <summary>Statut affichable d'une pièce : vérifiée, refusée (boutique refusée) ou en revue.</summary>
    private static string ResolveDocStatus(KybDocument doc, KybStatus sellerStatus) =>
        doc.VerifiedAtUtc.HasValue ? "Verified"
        : sellerStatus == KybStatus.Rejected ? "Rejected"
        : "InReview";
}
