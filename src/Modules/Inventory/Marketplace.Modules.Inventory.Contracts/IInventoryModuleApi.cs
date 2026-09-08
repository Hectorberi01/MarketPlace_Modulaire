namespace Marketplace.Modules.Inventory.Contracts;

/// <summary>
/// API in-process publique du module Inventory. Permet à Cart/Ordering de
/// vérifier la disponibilité d'un SKU avant d'autoriser l'ajout / la commande.
/// </summary>
public interface IInventoryModuleApi
{
    Task<AvailabilitySummary> GetAvailabilityAsync(string sku, CancellationToken cancellationToken = default);

    Task<bool> IsInStockAsync(string sku, int quantity, CancellationToken cancellationToken = default);

    /// <summary>Réserve du stock pour une commande (étape du Saga Ordering). Vrai si réussi.</summary>
    Task<bool> TryReserveAsync(string sku, Guid locationId, Guid orderId, int quantity, CancellationToken cancellationToken = default);

    /// <summary>Libère la réservation d'une commande (compensation : paiement échoué / annulation).</summary>
    Task ReleaseReservationAsync(string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Confirme la vente : décrémente le stock physique et solde la réservation.</summary>
    Task<bool> ConfirmReservationAsync(string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adresse d'un lieu d'expédition — OÙ LE COLIS SE RAMASSE.
    ///
    /// Ajouté pour la livraison par coursier : une expédition porte l'identifiant du lieu
    /// d'où elle part, mais rien ne permettait d'en obtenir l'adresse hors du module. Le
    /// coursier a besoin de la commune et surtout du POINT DE REPÈRE, sans quoi il ne
    /// trouve pas la boutique — au Bénin, la rue est souvent inexistante.
    /// </summary>
    Task<FulfillmentLocationSummary?> GetLocationAsync(Guid locationId, CancellationToken cancellationToken = default);
}
