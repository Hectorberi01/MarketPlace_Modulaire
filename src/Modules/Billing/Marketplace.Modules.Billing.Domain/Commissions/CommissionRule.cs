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

        // ─────────────────────────────────────────────────────────────────────────
        // UN PLANCHER OU UN PLAFOND NÉGATIF N'A PAS DE SENS, ET RIEN NE LE REFUSAIT.
        //
        // Seul `max < min` était vérifié. Un plancher à −500 passait donc, et `Compute`
        // l'applique : la commission « bornée par le bas » devient un montant NÉGATIF,
        // c'est-à-dire que la plateforme paie le vendeur en plus de sa vente. Personne
        // n'écrit cela volontairement — c'est le signe moins de trop, sur un écran où
        // les deux champs sont facultatifs et donc rarement relus.
        // ─────────────────────────────────────────────────────────────────────────
        if (minFee is < 0m || maxFee is < 0m)
        {
            return Error.Validation(
                "billing.bounds_negative",
                "Le plancher et le plafond de commission ne peuvent pas être négatifs.");
        }

        if (minFee is { } min && maxFee is { } max && max < min)
        {
            return Error.Validation("billing.bounds_invalid", "MaxFee doit être supérieur ou égal à MinFee.");
        }

        var devise = NormaliserDevise(currency);
        if (devise.IsFailure)
        {
            return devise.Error;
        }

        return new CommissionRule(
            CommissionRuleId.New(), scope, scope == CommissionScope.Global ? null : targetId,
            rate, fixedFee, devise.Value, minFee, maxFee, effectiveFromUtc);
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// UNE DEVISE VIDE PERSISTAIT, ET LE BARÈME NE VOULAIT PLUS RIEN DIRE.
    ///
    /// Le validateur `Length(3)` n'existait QUE pour la création : aucun
    /// `UpdateCommissionRuleCommandValidator` n'a jamais été écrit, et `Update`
    /// écrivait `currency.Trim().ToUpperInvariant()` sans condition. Vider le champ
    /// devise dans le formulaire d'édition — un champ texte libre — enregistrait donc
    /// une chaîne vide.
    ///
    /// Ce qui en découle : `FixedFee`, `MinFee` et `MaxFee` sont des MONTANTS. Sans
    /// devise, ils ne désignent rien, et l'arrondi de `Compute` suit une devise
    /// inconnue. L'écran affiche alors un barème dont personne ne peut dire s'il est
    /// en francs ou en euros.
    ///
    /// LE GARDE EST DANS L'AGRÉGAT, pas dans un validateur de commande : c'est le seul
    /// endroit que les deux chemins — création et modification — traversent forcément,
    /// et le seul qu'un appelant futur ne peut pas contourner. C'est précisément parce
    /// que le validateur de création existait et que celui de modification manquait que
    /// le défaut a pu vivre.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static Result<string> NormaliserDevise(string? currency)
    {
        var code = currency?.Trim().ToUpperInvariant() ?? string.Empty;

        // Trois lettres exactement, comme ISO 4217. On ne vérifie pas que le code
        // EXISTE : la liste vit ailleurs, elle bouge, et refuser un code inconnu
        // bloquerait une place qui s'ouvre à un pays de plus. Trois lettres suffisent à
        // écarter le vide et la saisie manifestement fausse, qui sont les deux cas vus.
        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
        {
            return Error.Validation(
                "billing.currency_invalid",
                "La devise doit être un code à trois lettres (XOF, EUR, USD…).");
        }

        return code;
    }


    /// <summary>
    /// Calcule la commission prélevée sur un montant brut, bornée si nécessaire.
    ///
    /// L'ARRONDI SUIT LA DEVISE DE LA RÈGLE, plus deux décimales en dur. La règle porte
    /// déjà sa <see cref="Currency"/> — le barème est libellé dans une devise, sinon
    /// <see cref="FixedFee"/>, <see cref="MinFee"/> et <see cref="MaxFee"/> ne
    /// voudraient rien dire. En franc CFA, l'arrondi à deux décimales fabriquait une
    /// commission à centimes qui remontait telle quelle au grand livre, alors qu'aucune
    /// écriture ne peut porter un dixième de franc.
    /// </summary>
    public decimal ComputeCommission(decimal grossAmount)
    {
        var commission = Money.Round(grossAmount * Rate, Currency) + FixedFee;

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

        // Même règle qu'à la création, et pour la même raison : voir `Create`. Elle est
        // répétée plutôt que factorisée parce que les deux méthodes rendent des types
        // différents ; la factoriser demanderait un aiguillage qui coûte plus à lire que
        // les quatre lignes qu'il économise.
        if (minFee is < 0m || maxFee is < 0m)
        {
            return Result.Failure(Error.Validation(
                "billing.bounds_negative",
                "Le plancher et le plafond de commission ne peuvent pas être négatifs."));
        }

        if (minFee is { } min && maxFee is { } max && max < min)
        {
            return  Result.Failure( Error.Validation("billing.bounds_invalid", "MaxFee doit être supérieur ou égal à MinFee."));
        }

        var devise = NormaliserDevise(currency);
        if (devise.IsFailure)
        {
            return Result.Failure(devise.Error);
        }

        Rate = rate;
        FixedFee = fixedFee;
        Currency = devise.Value;
        MinFee = minFee;
        MaxFee = maxFee;
        EffectiveFromUtc = effectiveFromUtc;
        return Result.Success();
    }
}
