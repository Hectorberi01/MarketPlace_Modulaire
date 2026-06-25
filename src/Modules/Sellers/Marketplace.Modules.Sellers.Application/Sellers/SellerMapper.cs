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
        seller.Status.ToString(),
        seller.KybStatus.ToString(),
        seller.CommissionRate,
        seller.Rating,
        seller.SalesCount,
        seller.KybDocuments
            .Select(d => new KybDocumentSummary(d.Id, d.Type.ToString(), d.FileUrl, d.VerifiedAtUtc))
            .ToList());
}
