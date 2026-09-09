using Marketplace.BuildingBlocks.Domain.Primitives;

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
    /// <summary>
    /// Montant en plus petite unité (centimes pour EUR/USD, unité pour XOF…).
    ///
    /// LA LISTE DES DEVISES SANS SUBDIVISION VIT DANS <see cref="Money"/>. Elle était
    /// recopiée ici — une troisième copie, à côté de celles de Stripe et de PayPal, et
    /// déjà divergente : celle-ci ignorait MGA et VUV. Trois listes qu'aucun test ne
    /// confrontait, pour une question qui n'a qu'une réponse.
    /// </summary>
    public static long ToMinorUnits(decimal amount, string currency)
    {
        var factor = Money.IsZeroDecimal(currency) ? 1m : 100m;
        return (long)decimal.Round(amount * factor, MidpointRounding.AwayFromZero);
    }
}
