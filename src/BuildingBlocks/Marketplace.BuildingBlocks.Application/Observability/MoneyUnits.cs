namespace Marketplace.BuildingBlocks.Application.Observability;

/// <summary>
/// Conversion d'un montant décimal vers la plus petite unité monétaire (entier),
/// pour les métriques financières (<c>payment_amount_total</c>,
/// <c>marketplace_revenue_total</c>…). Les devises « zéro décimale » (XOF, XAF,
/// JPY…) ne sont pas multipliées — crucial pour XOF (FedaPay) : 1000 XOF = 1000,
/// pas 100000.
/// </summary>
public static class MoneyUnits
{
    private static readonly HashSet<string> ZeroDecimal = new(StringComparer.OrdinalIgnoreCase)
    {
        "XOF", "XAF", "JPY", "KRW", "CLP", "VND", "GNF", "RWF", "UGX", "BIF", "DJF", "KMF", "PYG", "XPF"
    };

    /// <summary>Montant en plus petite unité (centimes pour EUR/USD, unité pour XOF…).</summary>
    public static long ToMinorUnits(decimal amount, string currency)
    {
        var factor = ZeroDecimal.Contains(currency) ? 1m : 100m;
        return (long)decimal.Round(amount * factor, MidpointRounding.AwayFromZero);
    }
}
