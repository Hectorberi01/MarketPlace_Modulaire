namespace Marketplace.Modules.Settlement.Application.Pricing;

/// <summary>
/// Options de tarification du module Settlement (section de configuration
/// « Pricing »). Le prix produit payé par l'acheteur = prix vendeur net +
/// commission plateforme + frais provider, les deux frais étant calculés en
/// fraction du prix vendeur net. Utilisées à la répartition du brut d'une ligne
/// lors de l'accrual (confirmation de commande).
///
/// Liées manuellement dans le SettlementModuleInstaller (même pattern que
/// Billing). Placées dans la couche Application car consommées par l'accrual
/// (la dépendance Infrastructure → Application interdit l'inverse).
/// </summary>
public sealed class PricingOptions
{
    /// <summary>
    /// Taux de commission plateforme, en fraction du prix vendeur net
    /// (0.10 = 10 %).
    /// </summary>
    public decimal PlatformCommissionRate { get; set; } = 0.10m;

    /// <summary>
    /// Taux des frais provider, en fraction du prix vendeur net (0.05 = 5 %).
    /// </summary>
    public decimal ProviderFeeRate { get; set; } = 0.05m;
}
