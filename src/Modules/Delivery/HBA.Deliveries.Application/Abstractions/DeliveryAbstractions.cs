using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using Marketplace.BuildingBlocks.Application.Abstractions;

namespace HBA.Deliveries.Application.Abstractions;

/// <summary>Frontière transactionnelle du module Delivery.</summary>
public interface IDeliveryUnitOfWork : IUnitOfWork
{
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA PART DU LIVREUR — RÉGLAGE, PAS CONSTANTE.
///
/// C'est une décision commerciale : elle se négocie, elle change, et elle doit
/// pouvoir changer sans redéploiement. Elle ne peut donc être ni une constante du
/// domaine, ni une valeur codée dans un handler.
///
/// Elle est lue au moment de la REMISE et figée sur la course avec son taux. Une
/// modification du réglage ne touche donc jamais les courses déjà livrées — voir
/// l'encadré sur <c>Delivery.DriverEarning</c>.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IDeliveryPayoutSettings
{
    /// <summary>Part du prix revenant au livreur, entre 0 et 1.</summary>
    decimal DriverShareRate { get; }
}

/// <summary>Réglages du suivi de position.</summary>
public static class DriverLocation
{
    /// <summary>
    /// Au-delà, une position est considérée comme périmée et le livreur n'est plus
    /// proposé. Deux minutes : assez pour absorber un trou de réseau, trop court
    /// pour qu'un livreur rentré chez lui sans se déconnecter bloque une course.
    /// </summary>
    public static readonly TimeSpan MaxPositionAge = TimeSpan.FromMinutes(2);
}

/// <summary>Un livreur en ligne, tel que le cache le connaît.</summary>
/// <param name="DriverId">Identifiant du livreur.</param>
/// <param name="Position">Dernière position transmise.</param>
/// <param name="ReportedAtUtc">Horodatage de cette position.</param>
public readonly record struct DriverPosition(DriverId DriverId, Coordinates Position, DateTime ReportedAtUtc);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES POSITIONS DES LIVREURS NE VONT PAS EN BASE.
///
/// Un livreur en ligne émet sa position toutes les cinq à quinze secondes. Avec
/// cent livreurs, cela fait entre sept et vingt écritures par seconde, sur une
/// donnée dont on ne conserve jamais l'historique et qui est périmée à la suivante.
/// PostgreSQL n'a rien à y gagner, et son journal de transactions beaucoup à y
/// perdre.
///
/// La position courante vit donc dans Redis, qui sait en plus répondre à « qui
/// est dans ce rayon » nativement (GEOADD / GEOSEARCH). L'agrégat
/// <see cref="Driver"/> ne garde qu'une dernière position connue, recopiée de
/// loin en loin, pour survivre à un vidage du cache.
///
/// ⚠️ UNE POSITION A UNE DATE DE PÉREMPTION. Un livreur dont le téléphone n'émet
/// plus depuis deux minutes est peut-être hors réseau, en tunnel, ou rentré chez
/// lui sans se déconnecter. Lui proposer une course, c'est perdre le délai
/// d'expiration avant de trouver quelqu'un d'autre. D'où
/// <see cref="DriverLocation.MaxPositionAge"/>, appliqué par l'implémentation.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IDriverLocationCache
{
    /// <summary>Enregistre la position courante d'un livreur.</summary>
    Task SetAsync(DriverId driverId, Coordinates position, CancellationToken cancellationToken = default);

    /// <summary>Retire un livreur du cache (passage hors ligne).</summary>
    Task RemoveAsync(DriverId driverId, CancellationToken cancellationToken = default);

    /// <summary>Position courante d'un livreur, si elle est fraîche.</summary>
    Task<DriverPosition?> GetAsync(DriverId driverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Livreurs présents dans un rayon, positions périmées exclues.
    ///
    /// Le centre n'est plus nullable depuis que la position d'un point de course
    /// est obligatoire : le repli « sans distance » qui existait ici était devenu
    /// du code injoignable, et un chemin injoignable se lit comme un chemin
    /// supporté.
    /// </summary>
    Task<IReadOnlyList<DriverPosition>> FindNearbyAsync(
        Coordinates center,
        double radiusKm,
        int limit = 20,
        CancellationToken cancellationToken = default);
}
