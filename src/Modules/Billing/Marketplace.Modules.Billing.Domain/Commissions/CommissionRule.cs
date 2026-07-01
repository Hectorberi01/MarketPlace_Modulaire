using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Billing.Domain.Commissions;

/// <summary>Identité forte d'une règle de commission.</summary>
public readonly record struct CommissionRuleId(Guid Value)
{
    public static CommissionRuleId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Périmètre d'une règle, de la moins à la plus spécifique.</summary>
public enum CommissionScope
{
    Global = 0,
    Category = 1,
    Seller = 2
}

/// <summary>
/// Règle de commission : définit combien la plateforme prélève. Distincte de
/// Payments (qui encaisse) et de Settlement (qui reverse). La résolution prend
/// la règle active la plus spécifique (Seller &gt; Category &gt; Global).
/// </summary>
public sealed class CommissionRule : AggregateRoot<CommissionRuleId>
{
    private CommissionRule()
    {
    }

    private CommissionRule(
        CommissionRuleId id, CommissionScope scope, Guid? targetId, decimal rate, decimal fixedFee,
        string currency, decimal? minFee, decimal? maxFee, DateTime effectiveFromUtc)
        : base(id)
    {
        Scope = scope;
        TargetId = targetId;
        Rate = rate;
        FixedFee = fixedFee;
        Currency = currency;
        MinFee = minFee;
        MaxFee = maxFee;
        EffectiveFromUtc = effectiveFromUtc;
        IsActive = true;
    }

    public CommissionScope Scope { get; private set; }
    public Guid? TargetId { get; private set; }
    public decimal Rate { get; private set; }
    public decimal FixedFee { get; private set; }
    public string Currency { get; private set; } = default!;
    public decimal? MinFee { get; private set; }
    public decimal? MaxFee { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>Spécificité : sert à départager les règles applicables.</summary>
    public int Priority => (int)Scope;

    public static Result<CommissionRule> Create(
        CommissionScope scope, Guid? targetId, decimal rate, decimal fixedFee,
        string currency, decimal? minFee, decimal? maxFee, DateTime effectiveFromUtc)
    {
        if (rate is < 0m or > 1m)
        {
            return Error.Validation("billing.rate_invalid", "Le taux de commission doit être compris entre 0 et 1.");
        }

        if (fixedFee < 0m)
        {
            return Error.Validation("billing.fixed_fee_invalid", "Le frais fixe ne peut pas être négatif.");
        }

        if (scope != CommissionScope.Global && (targetId is null || targetId == Guid.Empty))
        {
            return Error.Validation("billing.target_required", "Une règle Category/Seller exige une cible.");
        }

        if (minFee is { } min && maxFee is { } max && max < min)
        {
            return Error.Validation("billing.bounds_invalid", "MaxFee doit être supérieur ou égal à MinFee.");
        }

        return new CommissionRule(
            CommissionRuleId.New(), scope, scope == CommissionScope.Global ? null : targetId,
            rate, fixedFee, currency.Trim().ToUpperInvariant(), minFee, maxFee, effectiveFromUtc);
    }

    /// <summary>Calcule la commission prélevée sur un montant brut, bornée si nécessaire.</summary>
    public decimal ComputeCommission(decimal grossAmount)
    {
        var commission = decimal.Round(grossAmount * Rate, 2) + FixedFee;

        if (MinFee is { } min && commission < min)
        {
            commission = min;
        }

        if (MaxFee is { } max && commission > max)
        {
            commission = max;
        }

        return Math.Min(commission, grossAmount);
    }

    /// <summary>Vrai si la règle est active et en vigueur à l'instant donné.</summary>
    public bool IsApplicableAt(DateTime nowUtc) => IsActive && EffectiveFromUtc <= nowUtc;

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    /// <summary>Modifie la règle (taux, frais, devise, bornes, prise d'effet) avec revalidation. Le périmètre n'est pas modifiable.</summary>
    public Result Update(decimal rate, decimal fixedFee, string currency, decimal? minFee, decimal? maxFee, DateTime effectiveFromUtc)
    {
        if (rate is < 0m or > 1m)
        {
            return Result.Failure( Error.Validation("billing.rate_invalid", "Le taux de commission doit être compris entre 0 et 1."));
        }

        if (fixedFee < 0m)
        {
            return  Result.Failure( Error.Validation("billing.fixed_fee_invalid", "Le frais fixe ne peut pas être négatif."));
        }

        if (minFee is { } min && maxFee is { } max && max < min)
        {
            return  Result.Failure( Error.Validation("billing.bounds_invalid", "MaxFee doit être supérieur ou égal à MinFee."));
        }

        Rate = rate;
        FixedFee = fixedFee;
        Currency = currency.Trim().ToUpperInvariant();
        MinFee = minFee;
        MaxFee = maxFee;
        EffectiveFromUtc = effectiveFromUtc;
        return Result.Success();
    }
}
