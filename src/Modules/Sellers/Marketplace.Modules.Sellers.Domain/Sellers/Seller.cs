using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Sellers.Domain.Sellers.Events;

namespace Marketplace.Modules.Sellers.Domain.Sellers;

/// <summary>
/// Cycle de vie complet d'un vendeur : onboarding, KYB, boutique, commission,
/// coordonnées de payout (cf. dossier, module Sellers). C'est ce module qui fait
/// d'une plateforme une marketplace. Agrégat racine, possède ses documents KYB.
/// </summary>
public sealed class Seller : AggregateRoot<SellerId>
{
    private readonly List<KybDocument> _kybDocuments = new();

    private Seller()
    {
    }

    private Seller(SellerId id, Guid userId, string shopName, decimal commissionRate)
        : base(id)
    {
        UserId = userId;
        ShopName = shopName;
        CommissionRate = commissionRate;
        Status = SellerStatus.Pending;
        KybStatus = KybStatus.NotStarted;
        Rating = 0m;
        SalesCount = 0;
        CreatedOnUtc = DateTime.UtcNow;

        Raise(new SellerRegisteredDomainEvent(id.Value, userId, shopName));
    }

    public Guid UserId { get; private set; }
    public string ShopName { get; private set; } = default!;
    public string? LogoUrl { get; private set; }
    public string? Description { get; private set; }
    public SellerStatus Status { get; private set; }
    public KybStatus KybStatus { get; private set; }

    /// <summary>
    /// ⚠️ Neutralisé : la commission est désormais un taux fixe plateforme
    /// (Billing:DefaultCommissionRate). Ce champ n'est plus utilisé pour le calcul
    /// et n'est plus modifiable. Conservé en lecture seule (colonne EF, affichage
    /// dashboards/API) : ne pas supprimer pour éviter une migration.
    /// </summary>
    public decimal CommissionRate { get; private set; }
    public PayoutAccount? PayoutAccount { get; private set; }

    /// <summary>
    /// Informations société déclarées par le vendeur (jsonb, null par défaut).
    /// Renseignée à l'auto-inscription depuis l'app ; jamais une preuve — voir
    /// <see cref="SellerCompanyInfo"/> et le KYB.
    /// </summary>
    public SellerCompanyInfo? Metadata { get; private set; }

    public decimal Rating { get; private set; }
    public int SalesCount { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    public IReadOnlyCollection<KybDocument> KybDocuments => _kybDocuments.AsReadOnly();

    /// <summary>
    /// Onboarde un vendeur rattaché à un compte Identity. L'existence et la
    /// validité du compte sont vérifiées par l'Application (appel in-process à
    /// Identity) avant d'arriver ici.
    /// </summary>
    public static Result<Seller> Register(
        Guid userId, string shopName, decimal commissionRate, SellerCompanyInfo? metadata = null)
    {
        if (userId == Guid.Empty)
        {
            return Error.Validation("sellers.seller.user_required", "Le compte utilisateur est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(shopName))
        {
            return Error.Validation("sellers.seller.shop_name_required", "Le nom de la boutique est obligatoire.");
        }

        if (commissionRate is < 0m or > 1m)
        {
            return Error.Validation("sellers.seller.commission_invalid", "Le taux de commission doit être compris entre 0 et 1.");
        }

        var seller = new Seller(SellerId.New(), userId, shopName.Trim(), commissionRate);
        seller.Metadata = metadata;
        return seller;
    }

    /// <summary>
    /// Met à jour les informations société déclarées (édition du profil vendeur).
    /// Passer <c>null</c> efface la metadata. N'affecte ni le statut ni le KYB.
    /// </summary>
    public Result UpdateMetadata(SellerCompanyInfo? metadata)
    {
        Metadata = metadata;
        return Result.Success();
    }

    public Result UpdateProfile(string shopName, string? logoUrl, string? description)
    {
        if (string.IsNullOrWhiteSpace(shopName))
        {
            return Result.Failure(Error.Validation("sellers.seller.shop_name_required", "Le nom de la boutique est obligatoire."));
        }

        ShopName = shopName.Trim();
        LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        return Result.Success();
    }

    /// <summary>
    /// ⚠️ Neutralisé : la commission est désormais un taux fixe plateforme
    /// (Billing:DefaultCommissionRate). Ce champ n'est plus utilisé pour le calcul
    /// et n'est plus modifiable. Méthode conservée sans appelant (aucun endpoint /
    /// commande ne l'invoque) ; ne pas rebrancher.
    /// </summary>
    public Result SetCommissionRate(decimal commissionRate)
    {
        if (commissionRate is < 0m or > 1m)
        {
            return Result.Failure(Error.Validation("sellers.seller.commission_invalid", "Le taux de commission doit être compris entre 0 et 1."));
        }

        CommissionRate = commissionRate;
        return Result.Success();
    }

    public Result SetPayoutAccount(PayoutAccount payoutAccount)
    {
        PayoutAccount = payoutAccount;
        return Result.Success();
    }

    /// <summary>Ajoute une pièce KYB ; bascule la vérification en revue.</summary>
    public Result<KybDocument> AddKybDocument(KybDocumentType type, string fileUrl)
    {
        if (string.IsNullOrWhiteSpace(fileUrl))
        {
            return Error.Validation("sellers.kyb.file_required", "Le fichier de la pièce KYB est obligatoire.");
        }

        var document = new KybDocument(Guid.NewGuid(), type, fileUrl.Trim());
        _kybDocuments.Add(document);

        // Première soumission ou nouvelle tentative après un refus : la boutique
        // passe en revue. Une pièce ajoutée à une boutique DÉJÀ vérifiée (mise à
        // jour / renouvellement) est enregistrée sans dé-vérifier la boutique :
        // l'admin peut la revalider s'il le souhaite, sans interrompre l'activité.
        if (KybStatus is KybStatus.NotStarted or KybStatus.Rejected)
        {
            KybStatus = KybStatus.InReview;
        }

        return document;
    }

    /// <summary>
    /// Retire une pièce KYB de la boutique (le vendeur supprime un document qu'il a
    /// téléversé). La pièce est identifiée par son id ; l'appartenance à la boutique
    /// est garantie par la relation (la pièce est enfant de cet agrégat). Retourne
    /// l'entité retirée pour permettre à l'Application de nettoyer le stockage si
    /// besoin. Le retrait ne change pas le statut KYB de la boutique (une pièce
    /// vérifiée restée en base n'est pas re-demandée ; la re-validation admin reste
    /// possible via l'ajout d'une nouvelle pièce).
    /// </summary>
    public Result<KybDocument> RemoveKybDocument(Guid documentId)
    {
        var document = _kybDocuments.FirstOrDefault(d => d.Id == documentId);
        if (document is null)
        {
            return Error.NotFound("sellers.kyb.not_found", "Pièce KYB introuvable pour cette boutique.");
        }

        _kybDocuments.Remove(document);
        return document;
    }

    /// <summary>Valide le KYB (modération) : marque les pièces vérifiées.</summary>
    public Result ApproveKyb()
    {
        if (_kybDocuments.Count == 0)
        {
            return Result.Failure(Error.Conflict("sellers.kyb.no_documents", "Aucune pièce KYB à valider."));
        }

        foreach (var document in _kybDocuments)
        {
            document.MarkVerified();
        }

        KybStatus = KybStatus.Verified;
        Raise(new SellerKybVerifiedDomainEvent(Id.Value, UserId));
        return Result.Success();
    }

    public Result RejectKyb()
    {
        KybStatus = KybStatus.Rejected;
        return Result.Success();
    }

    /// <summary>Active le vendeur (KYB validé et coordonnées de payout requis).</summary>
    public Result Activate()
    {
        if (KybStatus != KybStatus.Verified)
        {
            return Result.Failure(Error.Conflict("sellers.seller.kyb_not_verified", "Le KYB doit être validé avant l'activation."));
        }

        if (PayoutAccount is null)
        {
            return Result.Failure(Error.Conflict("sellers.seller.payout_required", "Les coordonnées de reversement sont requises pour l'activation."));
        }

        Status = SellerStatus.Active;
        Raise(new SellerActivatedDomainEvent(Id.Value, UserId));
        return Result.Success();
    }

    public Result Suspend()
    {
        Status = SellerStatus.Suspended;
        return Result.Success();
    }

    public Result Reactivate()
    {
        if (KybStatus != KybStatus.Verified)
        {
            return Result.Failure(Error.Conflict("sellers.seller.kyb_not_verified", "Le KYB doit être validé."));
        }

        Status = SellerStatus.Active;
        return Result.Success();
    }

    /// <summary>Met à jour la note moyenne (0 à 5), alimentée par le module Reviews.</summary>
    public Result UpdateRating(decimal rating)
    {
        if (rating is < 0m or > 5m)
        {
            return Result.Failure(Error.Validation("sellers.seller.rating_invalid", "La note doit être comprise entre 0 et 5."));
        }

        Rating = rating;
        return Result.Success();
    }

    /// <summary>Incrémente le compteur de ventes, alimenté par Ordering.</summary>
    public void RecordSale() => SalesCount++;
}
