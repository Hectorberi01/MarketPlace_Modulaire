namespace Marketplace.Modules.Catalog.Infrastructure.Media;

/// <summary>
/// Santé observée du service de détourage local, partagée par le processus.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// POURQUOI « CONFIGURÉ » NE SUFFIT PAS À DIRE « DISPONIBLE »
///
/// `IImageProcessingAvailability` existe pour qu'une interface ne promette pas un
/// détourage qui n'aura pas lieu. Avec Cloudinary, « configuré » était une
/// approximation acceptable : un service managé tiers est là ou n'est pas là, et on
/// n'y peut rien. Avec un conteneur voisin, l'approximation devient fausse — le
/// conteneur peut être arrêté, en cours de redémarrage, ou en train de télécharger son
/// modèle. Répondre « oui, le détourage marche » dans ces cas-là ramène exactement le
/// faux badge « Détourée » que ce drapeau devait supprimer.
///
/// On ne sonde PAS activement : cela demanderait un service d'arrière-plan pour une
/// information cosmétique. On retient simplement le dernier échec. Après un échec,
/// la fonction est déclarée indisponible pendant <see cref="CooldownMinutes"/>, puis
/// redevient optimiste — sans quoi une panne d'une minute la condamnerait jusqu'au
/// prochain redéploiement.
/// ─────────────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed class RembgHealth
{
    private const int CooldownMinutes = 2;

    // `long` et Interlocked plutôt qu'un DateTime verrouillé : l'écriture vient de
    // n'importe quelle requête, la lecture de n'importe quelle autre.
    private long _lastFailureTicks;

    public void MarkSuccess() => Interlocked.Exchange(ref _lastFailureTicks, 0);

    public void MarkFailure() => Interlocked.Exchange(ref _lastFailureTicks, DateTime.UtcNow.Ticks);

    public bool IsHealthy
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastFailureTicks);
            if (ticks == 0)
            {
                return true;
            }

            return DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)
                   > TimeSpan.FromMinutes(CooldownMinutes);
        }
    }
}
