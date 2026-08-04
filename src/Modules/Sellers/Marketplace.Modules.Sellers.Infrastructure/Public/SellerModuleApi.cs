using Marketplace.BuildingBlocks.Domain.Geography;
using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Sellers.Application;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Sellers.Domain.Sellers;
using Marketplace.Modules.Sellers.Infrastructure.Persistence;

namespace Marketplace.Modules.Sellers.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Sellers. Lecture seule.
/// Permet par exemple à Catalog de vérifier qu'un vendeur est actif.
///
/// Toutes les lectures sont en cache-aside : cette API est appelée EN BOUCLE par la
/// fiche produit mobile (un appel par vendeur du produit). Voir SellersCacheKeys.
/// </summary>
internal sealed class SellerModuleApi : ISellerModuleApi
{
    private readonly SellersDbContext _dbContext;
    private readonly ICacheService _cache;

    public SellerModuleApi(SellersDbContext dbContext, ICacheService cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public Task<SellerSummary?> GetSellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            SellersCacheKeys.Seller(sellerId),
            async ct =>
            {
                var id = new SellerId(sellerId);
                var seller = await _dbContext.Sellers
                    .AsNoTracking()
                    .Include(s => s.KybDocuments)
                    .FirstOrDefaultAsync(s => s.Id == id, ct);

                return seller is null ? null : Map(seller);
            },
            SellersCacheKeys.SellerTtl,
            SellersCacheKeys.MissTtl,
            cancellationToken);

    public Task<SellerSummary?> GetSellerByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => _cache.GetOrCreateAsync(
            SellersCacheKeys.SellerByUser(userId),
            async ct =>
            {
                var seller = await _dbContext.Sellers
                    .AsNoTracking()
                    .Include(s => s.KybDocuments)
                    .FirstOrDefaultAsync(s => s.UserId == userId, ct);

                return seller is null ? null : Map(seller);
            },
            SellersCacheKeys.SellerTtl,
            SellersCacheKeys.MissTtl,
            cancellationToken);

    /// <summary>
    /// Vendeur actif ?
    ///
    /// Cette méthode faisait sa PROPRE requête (un AnyAsync). Elle se déduit
    /// désormais du résumé déjà en cache : dans le cas courant, elle ne coûte plus
    /// rien du tout — pas même un aller-retour Redis supplémentaire, puisque la
    /// fiche produit vient justement de charger ce même vendeur.
    ///
    /// Le statut est comparé sous forme de chaîne parce que c'est ainsi que
    /// SellerSummary le porte (Status.ToString()). Le nom de l'énumération est la
    /// source de vérité des deux côtés — pas une constante recopiée.
    /// </summary>
    public async Task<bool> IsActiveSellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
    {
        var seller = await GetSellerAsync(sellerId, cancellationToken);
        return seller is not null
            && string.Equals(seller.Status, nameof(SellerStatus.Active), StringComparison.Ordinal);
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
            .ToList(),
        seller.Metadata is null
            ? null
            : new SellerCompanyInfoSummary(
                seller.Metadata.LegalName, seller.Metadata.Rccm, seller.Metadata.Ifu,
                seller.Metadata.Address,
                seller.Metadata.Commune,
                BeninGeography.CommuneName(seller.Metadata.Commune),
                seller.Metadata.Activity,
                seller.Metadata.ManagerName, seller.Metadata.Phone));

    /// <summary>Statut affichable d'une pièce : vérifiée, refusée (boutique refusée) ou en revue.</summary>
    private static string ResolveDocStatus(KybDocument doc, KybStatus sellerStatus) =>
        doc.VerifiedAtUtc.HasValue ? "Verified"
        : sellerStatus == KybStatus.Rejected ? "Rejected"
        : "InReview";
}
