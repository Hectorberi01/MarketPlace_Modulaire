using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Tax.Domain.Taxes;

/// <summary>Identité forte d'une règle fiscale.</summary>
public readonly record struct TaxRuleId(Guid Value)
{
    public static TaxRuleId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Type de taxe.</summary>
public enum TaxType
{
    Vat = 0,
    SalesTax = 1,
    Exempt = 2
}

/// <summary>
/// Règle fiscale par juridiction (pays / zone UEMOA), éventuellement ciblée sur
/// une catégorie. Appliquée sur les commandes. La résolution retient la règle
/// active la plus spécifique (catégorie ciblée &gt; juridiction entière).
/// </summary>
public sealed class TaxRule : AggregateRoot<TaxRuleId>
{
    private TaxRule()
    {
    }

    private TaxRule(TaxRuleId id, string jurisdiction, Guid? categoryId, decimal rate, TaxType type, DateTime effectiveFromUtc)
        : base(id)
    {
        Jurisdiction = jurisdiction;
        CategoryId = categoryId;
        Rate = rate;
        Type = type;
        EffectiveFromUtc = effectiveFromUtc;
        IsActive = true;
    }

    public string Jurisdiction { get; private set; } = default!;
    public Guid? CategoryId { get; private set; }
    public decimal Rate { get; private set; }
    public TaxType Type { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>Spécificité : une règle ciblant une catégorie prime sur une règle de juridiction.</summary>
    public int Priority => CategoryId is null ? 0 : 1;

    public static Result<TaxRule> Create(string jurisdiction, Guid? categoryId, decimal rate, TaxType type, DateTime effectiveFromUtc)
    {
        if (string.IsNullOrWhiteSpace(jurisdiction))
        {
            return Error.Validation("tax.jurisdiction_required", "La juridiction est obligatoire.");
        }

        if (type != TaxType.Exempt && rate is < 0m or > 1m)
        {
            return Error.Validation("tax.rate_invalid", "Le taux doit être compris entre 0 et 1.");
        }

        return new TaxRule(
            TaxRuleId.New(), jurisdiction.Trim().ToUpperInvariant(),
            categoryId == Guid.Empty ? null : categoryId,
            type == TaxType.Exempt ? 0m : rate, type, effectiveFromUtc);
    }

    /// <summary>Calcule la taxe due sur un montant hors taxe.</summary>
    public decimal ComputeTax(decimal amount)
        => Type == TaxType.Exempt ? 0m : decimal.Round(amount * Rate, 2);

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LA TAXE CONTENUE DANS UN MONTANT TOUTES TAXES COMPRISES.
    ///
    /// L'INVERSE de <see cref="ComputeTax"/>, et non une variante. La place applique
    /// le régime « TVA comprise » : le prix saisi par le vendeur est le prix payé par
    /// l'acheteur. La taxe ne s'AJOUTE donc pas au total, elle s'y trouve déjà — il
    /// s'agit de l'en extraire pour la comptabilité et la facture.
    ///
    ///     TTC = HT × (1 + taux)   donc   TVA = TTC × taux / (1 + taux)
    ///
    /// LA CONFUSION À NE PAS FAIRE. Appliquer <c>TTC × taux</c> — le réflexe — donne
    /// un montant trop élevé : à 18 %, 11 800 F donneraient 2 124 F au lieu de
    /// 1 800 F, soit 324 F de TVA déclarée en trop sur chaque commande. C'est
    /// précisément parce que les deux formules se ressemblent que ce sont deux
    /// méthodes nommées, et pas un paramètre booléen.
    ///
    /// Arrondi à deux décimales, une seule fois, comme partout ailleurs dans le
    /// calcul monétaire de la place. Le HT est déduit par SOUSTRACTION chez
    /// l'appelant, jamais recalculé : c'est ce qui garantit que HT + TVA redonne
    /// exactement le TTC, sans centime perdu à l'arrondi.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public decimal ExtractIncludedTax(decimal grossAmount)
        => Type == TaxType.Exempt || Rate == 0m
            ? 0m
            : decimal.Round(grossAmount * Rate / (1m + Rate), 2);

    public bool IsApplicableAt(DateTime nowUtc) => IsActive && EffectiveFromUtc <= nowUtc;

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    /// <summary>Modifie la règle (juridiction, catégorie, taux, type, prise d'effet) avec revalidation.</summary>
    public Result Update(string jurisdiction, Guid? categoryId, decimal rate, TaxType type, DateTime effectiveFromUtc)
    {
        if (string.IsNullOrWhiteSpace(jurisdiction))
        {
            return  Result.Failure( Error.Validation("tax.jurisdiction_required", "La juridiction est obligatoire."));
        }

        if (type != TaxType.Exempt && rate is < 0m or > 1m)
        {
            return  Result.Failure( Error.Validation("tax.rate_invalid", "Le taux doit être compris entre 0 et 1."));
        }

        Jurisdiction = jurisdiction.Trim().ToUpperInvariant();
        CategoryId = categoryId == Guid.Empty ? null : categoryId;
        Rate = type == TaxType.Exempt ? 0m : rate;
        Type = type;
        EffectiveFromUtc = effectiveFromUtc;
        return Result.Success();
    }
}
