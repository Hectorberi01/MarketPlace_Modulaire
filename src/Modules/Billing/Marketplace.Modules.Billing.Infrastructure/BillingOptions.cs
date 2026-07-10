namespace Marketplace.Modules.Billing.Infrastructure;

/// <summary>
/// Options du module Billing (section de configuration « Billing »).
/// </summary>
public sealed class BillingOptions
{
    /// <summary>
    /// Taux de commission plateforme par défaut, en fraction (0.10 = 10 %).
    /// Appliqué à CHAQUE vente lorsqu'aucune règle de commission spécifique
    /// (Global / Catégorie / Vendeur) n'est définie. Évite le prélèvement nul
    /// silencieux : par défaut la plateforme prélève toujours ce taux fixe.
    /// </summary>
    public decimal DefaultCommissionRate { get; set; } = 0.10m;
}
