namespace Marketplace.Modules.Billing.Contracts;

/// <summary>
/// API in-process publique du module Billing. Settlement l'appelle pour connaître
/// la commission prélevée sur le revenu brut d'un vendeur (par catégorie), sans
/// accéder à la base des règles.
/// </summary>
public interface ICommissionModuleApi
{
    Task<CommissionResult> ComputeCommissionAsync(
        Guid sellerId, Guid categoryId, decimal grossAmount, string currency, CancellationToken cancellationToken = default);
}
