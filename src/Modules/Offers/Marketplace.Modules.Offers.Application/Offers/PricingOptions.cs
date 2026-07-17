namespace Marketplace.Modules.Offers.Application.Offers;

/// <summary>
/// Taux du modèle de prix « prix vendeur net + majoration plateforme ».
/// Le vendeur saisit son prix net ; la plateforme empile la commission et les frais
/// provider par-dessus pour obtenir le prix produit payé par l'acheteur.
/// Lié manuellement depuis la section de config « Pricing » dans l'installer
/// (même pattern que Payments/Billing : aucune dépendance IOptions).
///
/// NOTE : placé dans Offers.Application (et non Infrastructure) car le handler
/// l'injecte ; l'architecture impose Infrastructure -> Application, donc un type
/// d'Infrastructure ne peut pas être injecté dans un handler Application sans cycle.
/// L'installer (Infrastructure) le construit, le lie et l'enregistre en singleton.
/// </summary>
public sealed class PricingOptions
{
    /// <summary>Commission plateforme (fraction, ex. 0.10 = 10 %).</summary>
    public decimal PlatformCommissionRate { get; set; } = 0.10m;

    /// <summary>Frais provider (fraction, ex. 0.05 = 5 %).</summary>
    public decimal ProviderFeeRate { get; set; } = 0.05m;
}
