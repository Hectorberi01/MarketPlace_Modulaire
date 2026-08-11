namespace Marketplace.Modules.Inventory.Contracts;

/// <summary>
/// API in-process publique du module Inventory. Permet à Cart/Ordering de
/// vérifier la disponibilité d'un SKU avant d'autoriser l'ajout / la commande.
/// </summary>
public interface IInventoryModuleApi
{
    Task<AvailabilitySummary> GetAvailabilityAsync(string sku, CancellationToken cancellationToken = default);

    /// <summary>
    /// Un lieu d'expédition, par son identifiant. Nul s'il n'existe pas.
    ///
    /// Ajouté pour la logistique : le lieu d'expédition est le POINT DE COLLECTE
    /// d'une course. On pouvait jusqu'ici lister les lieux d'un propriétaire, pas
    /// en lire un seul — or l'expédition ne connaît que son identifiant, et
    /// balayer tous les lieux d'un vendeur pour en retrouver un serait une lecture
    /// large là où une lecture ponctuelle suffit.
    /// </summary>
    Task<FulfillmentLocationSummary?> GetLocationAsync(Guid locationId, CancellationToken cancellationToken = default);

    Task<bool> IsInStockAsync(string sku, int quantity, CancellationToken cancellationToken = default);

    /// <summary>Réserve du stock pour une commande (étape du Saga Ordering). Vrai si réussi.</summary>
    Task<bool> TryReserveAsync(string sku, Guid locationId, Guid orderId, int quantity, CancellationToken cancellationToken = default);

    /// <summary>Libère la réservation d'une commande (compensation : paiement échoué / annulation).</summary>
    Task ReleaseReservationAsync(string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Confirme la vente : décrémente le stock physique et solde la réservation.</summary>
    Task<bool> ConfirmReservationAsync(string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default);
}
