namespace Marketplace.Modules.Pricing.Domain.Promotions;

/// <summary>
/// Conditions d'éligibilité d'une promotion (montant/quantité minimum, première
/// commande). Mappé en jsonb. Value Object immuable.
/// </summary>
public sealed record PromotionConditions(
    decimal? MinAmount = null,
    int? MinQuantity = null,
    bool FirstOrderOnly = false)
{
    public static PromotionConditions None => new();

    /// <summary>Vérifie les conditions contre le contexte d'achat.</summary>
    public bool AreMet(decimal subtotal, int quantity, bool isFirstOrder)
    {
        if (MinAmount is { } min && subtotal < min)
        {
            return false;
        }

        if (MinQuantity is { } minQty && quantity < minQty)
        {
            return false;
        }

        if (FirstOrderOnly && !isFirstOrder)
        {
            return false;
        }

        return true;
    }
}
