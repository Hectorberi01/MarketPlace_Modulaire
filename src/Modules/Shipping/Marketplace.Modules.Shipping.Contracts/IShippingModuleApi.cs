namespace Marketplace.Modules.Shipping.Contracts;

/// <summary>
/// API in-process publique du module Shipping. Permet aux autres modules de
/// lire les expéditions d'une commande sans accéder à sa base.
/// </summary>
public interface IShippingModuleApi
{
    Task<IReadOnlyList<ShipmentSummary>> GetShipmentsByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Options de livraison applicables à une commune de destination.
    ///
    /// LE SEUL POINT DE VÉRITÉ DU PRIX DE LIVRAISON. Le BFF affichait auparavant sa
    /// propre grille codée en dur : Cotonou et Karimama, sept cents kilomètres
    /// d'écart, coûtaient le même prix, et rien ne rattachait le montant facturé au
    /// montant affiché. Tout écran qui montre un tarif et tout code qui en facture un
    /// doivent désormais passer par ici, avec la MÊME commune.
    ///
    /// Ne renvoie JAMAIS une liste vide : sans zone configurée, ce sont les forfaits
    /// de secours qui répondent. Une grille absente doit dégrader le prix, jamais
    /// interrompre la vente.
    /// </summary>
    Task<IReadOnlyList<ShippingRateOption>> GetRatesForCommuneAsync(string? communeCode, CancellationToken cancellationToken = default);
}
