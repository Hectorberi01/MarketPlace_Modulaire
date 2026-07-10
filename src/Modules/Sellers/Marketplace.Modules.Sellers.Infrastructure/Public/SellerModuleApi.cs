using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Sellers.Domain.Sellers;
using Marketplace.Modules.Sellers.Infrastructure.Persistence;

namespace Marketplace.Modules.Sellers.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Sellers. Lecture seule.
/// Permet par exemple à Catalog de vérifier qu'un vendeur est actif.
/// </summary>
internal sealed class SellerModuleApi : ISellerModuleApi
{
    private readonly SellersDbContext _dbContext;

    public SellerModuleApi(SellersDbContext dbContext)
        => _dbContext = dbContext;

    public async Task<SellerSummary?> GetSellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        var id = new SellerId(sellerId);
        var seller = await _dbContext.Sellers
            .AsNoTracking()
            .Include(s => s.KybDocuments)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        return seller is null ? null : Map(seller);
    }

    public async Task<SellerSummary?> GetSellerByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var seller = await _dbContext.Sellers
            .AsNoTracking()
            .Include(s => s.KybDocuments)
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);

        return seller is null ? null : Map(seller);
    }

    public async Task<bool> IsActiveSellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        var id = new SellerId(sellerId);
        return await _dbContext.Sellers
            .AsNoTracking()
            .AnyAsync(s => s.Id == id && s.Status == SellerStatus.Active, cancellationToken);
    }

    private static SellerSummary Map(Seller seller) => new(
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
            .ToList());

    /// <summary>Statut affichable d'une pièce : vérifiée, refusée (boutique refusée) ou en revue.</summary>
    private static string ResolveDocStatus(KybDocument doc, KybStatus sellerStatus) =>
        doc.VerifiedAtUtc.HasValue ? "Verified"
        : sellerStatus == KybStatus.Rejected ? "Rejected"
        : "InReview";
}
