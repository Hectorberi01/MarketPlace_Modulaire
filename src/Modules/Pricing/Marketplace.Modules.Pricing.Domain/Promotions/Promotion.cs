using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Pricing.Domain.Promotions.Events;

namespace Marketplace.Modules.Pricing.Domain.Promotions;

/// <summary>
/// Réduction appliquée par-dessus le prix de base, avec un financeur explicite
/// (cf. dossier, Promotion). Le champ FundedBy détermine la répartition au
/// payout : une réduction Seller diminue le revenu vendeur, une réduction
/// Platform est portée par la plateforme.
/// </summary>
public sealed class Promotion : AggregateRoot<PromotionId>
{
    private List<Guid> _targets = new();

    private Promotion()
    {
    }

    private Promotion(
        PromotionId id,
        PromotionOwnerType ownerType,
        Guid ownerId,
        FundedBy fundedBy,
        PromotionType type,
        decimal value,
        PromotionScopeType scopeType,
        IEnumerable<Guid> targets,
        PromotionConditions conditions,
        string? code,
        DateRange period,
        int usageLimit,
        int perUserLimit)
        : base(id)
    {
        OwnerType = ownerType;
        OwnerId = ownerId;
        FundedBy = fundedBy;
        Type = type;
        Value = value;
        ScopeType = scopeType;
        _targets.AddRange(targets);
        Conditions = conditions;
        Code = code;
        Period = period;
        UsageLimit = usageLimit;
        PerUserLimit = perUserLimit;
        UsedCount = 0;
        Status = PromotionStatus.Draft;

        Raise(new PromotionCreatedDomainEvent(id.Value, type.ToString(), scopeType.ToString()));
    }

    public PromotionOwnerType OwnerType { get; private set; }
    public Guid OwnerId { get; private set; }
    public FundedBy FundedBy { get; private set; }
    public PromotionType Type { get; private set; }
    public decimal Value { get; private set; }
    public PromotionScopeType ScopeType { get; private set; }
    public IReadOnlyCollection<Guid> Targets => _targets.AsReadOnly();
    public PromotionConditions Conditions { get; private set; } = PromotionConditions.None;
    public string? Code { get; private set; }
    public DateRange Period { get; private set; } = default!;

    /// <summary>Plafond GLOBAL d'utilisations. 0 = illimité.</summary>
    public int UsageLimit { get; private set; }

    /// <summary>
    /// Plafond d'utilisations PAR ACHETEUR. 0 = illimité.
    ///
    /// Sans ce plafond, un coupon « BIENVENUE10 » plafonné à 1 000 usages globaux était
    /// consommé 1 000 fois par la MÊME personne. Le plafond global protège la
    /// plateforme du volume, jamais de l'abus individuel — ce sont deux limites
    /// distinctes, et il en fallait deux.
    /// </summary>
    public int PerUserLimit { get; private set; }

    public int UsedCount { get; private set; }
    public PromotionStatus Status { get; private set; }

    public static Result<Promotion> Create(
        PromotionOwnerType ownerType,
        Guid ownerId,
        FundedBy fundedBy,
        PromotionType type,
        decimal value,
        PromotionScopeType scopeType,
        IEnumerable<Guid> targets,
        PromotionConditions conditions,
        string? code,
        DateRange period,
        int usageLimit,
        int perUserLimit = 0)
    {
        if (ownerId == Guid.Empty)
        {
            return Error.Validation("pricing.promotion.owner_required", "Le propriétaire de la promotion est obligatoire.");
        }

        if (type == PromotionType.Percentage && value is < 0m or > 100m)
        {
            return Error.Validation("pricing.promotion.percentage_invalid", "Le pourcentage doit être compris entre 0 et 100.");
        }

        if (type == PromotionType.FixedAmount && value < 0m)
        {
            return Error.Validation("pricing.promotion.amount_invalid", "Le montant fixe ne peut pas être négatif.");
        }

        if (usageLimit < 0)
        {
            return Error.Validation("pricing.promotion.usage_invalid", "Le plafond d'utilisation ne peut pas être négatif.");
        }

        if (perUserLimit < 0)
        {
            return Error.Validation("pricing.promotion.per_user_limit_invalid", "Le plafond par acheteur ne peut pas être négatif.");
        }

        var requiresTargets = scopeType is PromotionScopeType.Product or PromotionScopeType.Category or PromotionScopeType.Seller;
        var targetList = targets?.Where(t => t != Guid.Empty).Distinct().ToList() ?? new List<Guid>();
        if (requiresTargets && targetList.Count == 0)
        {
            return Error.Validation("pricing.promotion.targets_required", "Ce périmètre exige au moins une cible.");
        }

        return new Promotion(
            PromotionId.New(), ownerType, ownerId, fundedBy, type, value, scopeType,
            targetList, conditions ?? PromotionConditions.None,
            string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant(),
            period, usageLimit, perUserLimit);
    }

    public Result Activate()
    {
        if (Status == PromotionStatus.Expired)
        {
            return Result.Failure(Error.Conflict("pricing.promotion.expired", "Une promotion expirée ne peut pas être réactivée."));
        }

        Status = PromotionStatus.Active;
        Raise(new PromotionActivatedDomainEvent(Id.Value));
        return Result.Success();
    }

    public Result Pause()
    {
        Status = PromotionStatus.Paused;
        return Result.Success();
    }

    public Result Expire()
    {
        Status = PromotionStatus.Expired;
        return Result.Success();
    }

    /// <summary>
    /// Consomme une utilisation de la promotion pour un acheteur donné.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// LE SEUL POINT OÙ UNE PROMOTION EST RÉELLEMENT DÉCOMPTÉE. ET IL SÉRIALISE.
    ///
    /// Cette méthode incrémente TOUJOURS `UsedCount` en cas de succès — donc elle
    /// salit TOUJOURS la ligne `promotions`. Combiné au jeton de concurrence `xmin`
    /// posé sur cet agrégat, cela a une conséquence décisive :
    ///
    ///   **toutes les rédemptions d'une même promotion se sérialisent sur sa ligne.**
    ///
    /// Deux commandes qui tentent de consommer le dernier exemplaire d'un coupon au
    /// même instant ne peuvent pas commiter toutes les deux : la seconde voit son
    /// UPDATE toucher 0 ligne et lève `DbUpdateConcurrencyException`. C'est ce qui
    /// rend le contrôle `buyerPriorRedemptions` ci-dessous FIABLE malgré le fait
    /// qu'il repose sur un COUNT lu juste avant : le COUNT et l'UPDATE sont dans la
    /// même transaction, et la ligne de promotion en est le point de rendez-vous
    /// obligé. Sans le jeton, ce contrôle serait un vœu pieux.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <param name="buyerPriorRedemptions">
    /// Nombre de fois que CET acheteur a DÉJÀ consommé cette promotion — compté dans la
    /// même transaction (voir IPromotionRedemptionRepository.CountForBuyerAsync).
    /// </param>
    public Result Redeem(int buyerPriorRedemptions)
    {
        if (Status != PromotionStatus.Active)
        {
            return Result.Failure(Error.Conflict("pricing.promotion.not_active", "La promotion n'est pas active."));
        }

        if (PerUserLimit > 0 && buyerPriorRedemptions >= PerUserLimit)
        {
            return Result.Failure(Error.Conflict("pricing.promotion.per_user_limit_reached", "Vous avez déjà utilisé cette promotion le nombre de fois autorisé."));
        }

        return RecordUsage();
    }

    /// <summary>
    /// Incrémente le compteur global d'utilisation, dans la limite du plafond.
    /// Primitive : passer par <see cref="Redeem"/>, qui applique aussi la limite par
    /// acheteur et le contrôle de statut.
    /// </summary>
    public Result RecordUsage()
    {
        if (UsageLimit > 0 && UsedCount >= UsageLimit)
        {
            return Result.Failure(Error.Conflict("pricing.promotion.usage_exceeded", "Le plafond d'utilisation de la promotion est atteint."));
        }

        UsedCount++;
        return Result.Success();
    }

    /// <summary>Vrai si la promotion est active, dans sa période et sous son plafond.</summary>
    public bool IsActiveAt(DateTime nowUtc)
        => Status == PromotionStatus.Active
           && Period.Contains(nowUtc)
           && (UsageLimit == 0 || UsedCount < UsageLimit);

    /// <summary>Vrai si la promotion s'applique au contexte d'achat fourni.</summary>
    public bool AppliesTo(
        Guid productId,
        Guid categoryId,
        Guid sellerId,
        decimal subtotal,
        int quantity,
        bool isFirstOrder,
        string? providedCode)
    {
        // Code promo : si la promo en exige un, il doit correspondre.
        if (Code is not null && !string.Equals(Code, providedCode?.Trim().ToUpperInvariant(), StringComparison.Ordinal))
        {
            return false;
        }

        if (!Conditions.AreMet(subtotal, quantity, isFirstOrder))
        {
            return false;
        }

        return ScopeType switch
        {
            PromotionScopeType.Product => _targets.Contains(productId),
            PromotionScopeType.Category => _targets.Contains(categoryId),
            PromotionScopeType.Seller => _targets.Contains(sellerId),
            PromotionScopeType.Cart => true,
            PromotionScopeType.Global => true,
            _ => false
        };
    }

    /// <summary>Calcule le montant de réduction sur un montant donné.</summary>
    public decimal ComputeDiscount(decimal amount)
        => Type switch
        {
            PromotionType.Percentage => decimal.Round(amount * Value / 100m, 2),
            PromotionType.FixedAmount => Math.Min(Value, amount),
            PromotionType.FreeShipping => 0m,   // porté par Shipping, pas sur le prix article
            PromotionType.BuyXGetY => 0m,        // nécessite le détail panier (calculé en amont)
            _ => 0m
        };
}
