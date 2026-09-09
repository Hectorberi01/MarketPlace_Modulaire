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
    /// Neutralisé : la commission est désormais un taux fixe plateforme
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

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════
    /// LA MÉMOIRE DE LA SANCTION, PARCE QUE `Status` NE L'EST PAS.
    ///
    /// `Status` est un champ unique, écrasé à chaque transition. Un vendeur suspendu
    /// qui repassait par fermeture puis demande de réactivation se présentait donc à
    /// l'administrateur comme une banale demande de réouverture : l'agrégat avait
    /// perdu toute trace de la sanction entre-temps.
    ///
    /// Ces trois champs survivent aux transitions. Ils ne bloquent rien par
    /// eux-mêmes — ce sont les gardes ci-dessous qui bloquent — mais ils rendent
    /// l'historique lisible à qui décide, et ils sont ce qu'il faut pour qu'une
    /// deuxième suspension ne ressemble pas à la première.
    /// ═══════════════════════════════════════════════════════════════════════════
    /// </summary>
    public DateTime? SuspendedOnUtc { get; private set; }

    /// <summary>Motif de la suspension EN COURS (effacé à la levée). Null si jamais suspendu.</summary>
    public string? SuspensionReason { get; private set; }

    /// <summary>
    /// Motif du DERNIER rejet de KYB, tel qu'affiché au vendeur. Effacé dès qu'une
    /// nouvelle pièce relance la revue, et à la validation.
    /// </summary>
    public string? KybRejectionReason { get; private set; }

    /// <summary>Nombre total de suspensions subies. N'est JAMAIS remis à zéro.</summary>
    public int SuspensionCount { get; private set; }

    /// <summary>Vrai tant qu'une suspension est en cours.</summary>
    public bool IsSuspended => Status == SellerStatus.Suspended;

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
    /// Neutralisé : la commission est désormais un taux fixe plateforme
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

            // Le motif décrit le rejet PRÉCÉDENT. Le laisser affiché pendant que le
            // dossier est de nouveau en revue ferait croire à un second refus.
            KybRejectionReason = null;
        }

        return document;
    }

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════
    /// RETIRE UNE PIÈCE KYB — MAIS JAMAIS UNE PIÈCE DÉJÀ VÉRIFIÉE.
    ///
    /// Cette méthode ne regardait rien. L'enchaînement suivant était donc ouvert :
    /// téléverser une pièce, faire valider le dossier, supprimer la pièce. On
    /// obtenait une boutique `Verified` avec ZÉRO justificatif en base — activable,
    /// payable, et sans le moindre document derrière la conformité. Le dossier
    /// pouvait être vidé par son propre titulaire, après validation, sans trace et
    /// sans nouvelle revue.
    ///
    /// La règle porte sur LA PIÈCE, pas sur le statut de la boutique. Une pièce
    /// qu'un modérateur a examinée et marquée vérifiée est une décision de
    /// conformité : elle appartient au dossier, plus au vendeur. Une pièce non
    /// encore vérifiée reste supprimable — c'est le cas légitime du téléversement
    /// erroné qu'on corrige.
    ///
    /// Ce qu'il faut faire à la place d'une suppression : ajouter la pièce à jour.
    /// `AddKybDocument` repasse le dossier en revue quand c'est nécessaire, et la
    /// pièce périmée reste consultable — ce qui est précisément l'intérêt d'un
    /// dossier de conformité.
    /// ═══════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <remarks>
    /// Retourne l'entité retirée pour permettre à l'Application de nettoyer le
    /// stockage objet. Le retrait ne change pas le statut KYB de la boutique.
    /// </remarks>
    public Result<KybDocument> RemoveKybDocument(Guid documentId)
    {
        var document = _kybDocuments.FirstOrDefault(d => d.Id == documentId);
        if (document is null)
        {
            return Error.NotFound("sellers.kyb.not_found", "Pièce KYB introuvable pour cette boutique.");
        }

        if (document.VerifiedAtUtc is not null)
        {
            return Error.Conflict(
                "sellers.kyb.document_verified",
                "Cette pièce a été validée et fait partie du dossier de conformité : elle ne peut plus être supprimée. "
                + "Téléversez une pièce à jour si elle a changé.");
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
        KybRejectionReason = null;
        Raise(new SellerKybVerifiedDomainEvent(Id.Value, UserId));
        return Result.Success();
    }

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════
    /// REJET DU KYB — AVEC UN MOTIF, ET ANNONCÉ.
    ///
    /// L'ancienne version écrivait un statut, sans motif et sans événement. Aucun
    /// handler de notification n'existait : le vendeur découvrait « Rejected » sur
    /// son écran de compte, un jour où il pensait à y aller, sans savoir ce qui
    /// n'allait pas. Le comportement observé en pratique est alors toujours le
    /// même — il re-téléverse exactement la pièce refusée, et le dossier repart en
    /// revue pour rien.
    ///
    /// Le motif est ce qui transforme un refus en action possible. Il est donc
    /// obligatoire ici, dans le domaine, et pas seulement dans un validateur : un
    /// rejet sans motif n'est pas un rejet, c'est un mur.
    /// ═══════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result RejectKyb(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(Error.Validation(
                "sellers.kyb.reason_required",
                "Le motif du rejet est obligatoire : le vendeur doit savoir quoi corriger."));
        }

        KybStatus = KybStatus.Rejected;
        KybRejectionReason = reason.Trim();

        Raise(new SellerKybRejectedDomainEvent(Id.Value, UserId, KybRejectionReason));
        return Result.Success();
    }

    /// <summary>Active le vendeur (KYB validé et coordonnées de payout requis).</summary>
    public Result Activate()
    {
        // UNE SUSPENSION NE SE LÈVE PAS PAR LA PORTE DE L'ACTIVATION.
        //
        // `Activate` ne regardait pas le statut courant : POST /activate remettait
        // donc un vendeur suspendu en vente, sans passer par la moindre décision
        // nommée « lever la suspension » et sans que rien ne l'enregistre. Le geste
        // existe désormais séparément (`LiftSuspension`) ; celui-ci le refuse.
        if (Status == SellerStatus.Suspended)
        {
            return Result.Failure(Error.Conflict(
                "sellers.seller.suspended",
                "La boutique est suspendue : la suspension doit être levée explicitement."));
        }

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

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════
    /// SUSPENSION PAR L'ADMINISTRATEUR. ELLE DOIT SORTIR DU MODULE.
    ///
    /// L'ancienne version écrivait `Status` et rendait la main : aucun événement,
    /// donc aucun effet hors de Sellers. La vitrine du vendeur restait servie, ses
    /// offres restaient dans les fiches produit, et lui-même n'était pas prévenu.
    /// La sanction n'existait que dans une colonne.
    ///
    /// L'événement levé ici est ce qui la rend réelle : Catalog dépublie, et
    /// Notifications informe le vendeur AVEC LE MOTIF — sans quoi il ne peut rien
    /// corriger et découvre la sanction à la chute de ses ventes.
    /// ═══════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <param name="reason">
    /// Motif communiqué au vendeur. Facultatif dans le domaine — un administrateur
    /// peut avoir à suspendre d'urgence — mais la surface d'administration, elle,
    /// l'exige : un motif absent est une sanction inexplicable.
    /// </param>
    public Result Suspend(string? reason = null)
    {
        if (Status == SellerStatus.Suspended)
        {
            return Result.Failure(Error.Conflict(
                "sellers.seller.already_suspended", "La boutique est déjà suspendue."));
        }

        Status = SellerStatus.Suspended;
        SuspendedOnUtc = DateTime.UtcNow;
        SuspensionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        SuspensionCount++;

        Raise(new SellerSuspendedDomainEvent(Id.Value, UserId, SuspensionReason));
        return Result.Success();
    }

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════
    /// LA LEVÉE DE SUSPENSION : LA SEULE SORTIE, ET ELLE EST EXPLICITE.
    ///
    /// Remplace un `Reactivate()` qui n'avait AUCUN appelant dans le dépôt et
    /// AUCUNE garde de statut : il faisait passer n'importe quel vendeur à `Active`
    /// dès lors que son KYB était validé. Une méthode publique sans appelant et
    /// sans garde n'est pas du code mort, c'est une porte qui attend son premier
    /// utilisateur.
    ///
    /// Lever une suspension est une DÉCISION, distincte de « activer » et distincte
    /// d'« approuver une réactivation ». Elle a donc son propre geste, son propre
    /// événement, et elle laisse `SuspensionCount` intact : on lève la sanction, on
    /// n'efface pas qu'elle a eu lieu.
    /// ═══════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Result LiftSuspension()
    {
        if (Status != SellerStatus.Suspended)
        {
            return Result.Failure(Error.Conflict(
                "sellers.seller.not_suspended", "La boutique n'est pas suspendue."));
        }

        if (KybStatus != KybStatus.Verified)
        {
            return Result.Failure(Error.Conflict("sellers.seller.kyb_not_verified", "Le KYB doit être validé."));
        }

        // Le motif de la suspension EN COURS disparaît avec elle ; le compteur, non.
        SuspensionReason = null;
        SuspendedOnUtc = null;
        Status = SellerStatus.Active;

        Raise(new SellerSuspensionLiftedDomainEvent(Id.Value, UserId));
        return Result.Success();
    }

    /// <summary>
    /// Fermeture demandée par le vendeur lui-même (suppression partielle). Le compte
    /// bascule en <see cref="SellerStatus.Closed"/> : l'événement déclenche le retrait
    /// des produits de la vente (module Catalog). Le compte n'est PAS effacé — seul
    /// l'admin peut le supprimer définitivement.
    /// </summary>
    public Result RequestClosure()
    {
        if (Status is SellerStatus.Closed or SellerStatus.PendingReactivation)
        {
            return Result.Failure(Error.Conflict("sellers.seller.already_closed", "Le compte est déjà fermé."));
        }

        // ═══════════════════════════════════════════════════════════════════════
        // LE BLANCHIMENT DE SUSPENSION SE COUPE ICI.
        //
        // L'enchaînement était entièrement praticable, chaque étape passant sa
        // propre garde : l'admin suspend, le vendeur ferme lui-même sa boutique
        // (cette méthode acceptait un suspendu), il demande la réactivation
        // (`RequestReactivation` exige `Closed` — il l'est), et l'admin approuve
        // (`ApproveReactivation` ne regarde que `PendingReactivation` et le KYB).
        // Le vendeur suspendu pour fraude se présentait ainsi comme une banale
        // demande de réouverture, sanction effacée.
        //
        // Le maillon est celui-ci : un compte suspendu ne se ferme pas tout seul.
        // La suspension se lève par décision d'administrateur, ou pas du tout.
        // ═══════════════════════════════════════════════════════════════════════
        if (Status == SellerStatus.Suspended)
        {
            return Result.Failure(Error.Conflict(
                "sellers.seller.suspended",
                "Une boutique suspendue ne peut pas être fermée : contactez le support."));
        }

        Status = SellerStatus.Closed;
        Raise(new SellerClosedDomainEvent(Id.Value, UserId));
        return Result.Success();
    }

    /// <summary>Le vendeur (compte fermé) demande la réactivation : passe en attente de validation admin.</summary>
    public Result RequestReactivation()
    {
        if (Status != SellerStatus.Closed)
        {
            return Result.Failure(Error.Conflict("sellers.seller.not_closed", "Seul un compte fermé peut demander une réactivation."));
        }

        Status = SellerStatus.PendingReactivation;
        return Result.Success();
    }

    /// <summary>
    /// L'admin approuve la demande de réactivation : le compte redevient actif et
    /// l'événement permet au Catalog de reprendre le vendeur en compte. Les produits
    /// restés en brouillon depuis la fermeture sont republiés par le vendeur.
    /// </summary>
    public Result ApproveReactivation()
    {
        if (Status is not (SellerStatus.PendingReactivation or SellerStatus.Closed))
        {
            return Result.Failure(Error.Conflict("sellers.seller.no_reactivation_request", "Aucune demande de réactivation en cours."));
        }

        if (KybStatus != KybStatus.Verified)
        {
            return Result.Failure(Error.Conflict("sellers.seller.kyb_not_verified", "Le KYB doit être validé."));
        }

        Status = SellerStatus.Active;
        Raise(new SellerReactivatedDomainEvent(Id.Value, UserId));
        return Result.Success();
    }

    /// <summary>
    /// Prépare la suppression DÉFINITIVE (admin) : émet l'événement qui purge les
    /// produits du vendeur avant que l'agrégat ne soit retiré du dépôt. À appeler
    /// juste avant <c>Remove</c> côté Application.
    /// </summary>
    public Result MarkForDeletion()
    {
        Raise(new SellerDeletedDomainEvent(Id.Value, UserId));
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

    /// <summary>
    /// Fixe le compteur de ventes à une valeur recalculée depuis la source (Ordering).
    /// Préféré à <see cref="RecordSale"/> pour l'alimentation événementielle : poser le
    /// total exact est idempotent, alors qu'incrémenter double-compterait si l'événement
    /// « commande confirmée » est rejoué.
    /// </summary>
    public void SetSalesCount(int count) => SalesCount = count < 0 ? 0 : count;
}
