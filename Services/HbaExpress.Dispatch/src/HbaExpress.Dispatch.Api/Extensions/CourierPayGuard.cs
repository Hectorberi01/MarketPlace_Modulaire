using HbaExpress.Dispatch.Application.Jobs;
using System.Globalization;

namespace HbaExpress.Dispatch.Api.Extensions;

/// <summary>
/// Lit la part du coursier, et REFUSE DE DÉMARRER si elle est absente ou aberrante.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UN SERVICE QUI NE SAIT PAS CE QU'IL DOIT PAYER NE DOIT PAS TOURNER.
///
/// La règle de tarification définitive n'est pas arrêtée (CDC §5.3). En attendant, le
/// coursier touche une part des frais de livraison — et cette part est une décision
/// commerciale, pas une constante technique.
///
/// L'alternative aurait été un défaut dans le code. Elle est pire qu'elle n'en a l'air :
/// personne ne l'aurait choisie, personne ne l'aurait relue, et on l'aurait découverte au
/// premier relevé de compte. Démarrer sans savoir combien payer, c'est se réserver le
/// droit de se tromper en silence sur toutes les courses à venir.
///
/// Échouer au démarrage se voit tout de suite, et le message dit quoi écrire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class CourierPayGuard
{
    public static CourierPayRule Resolve(IConfiguration configuration)
    {
        var raw = configuration[CourierPayRule.ShareKey];

        // CULTURE INVARIANTE : « 0.8 » doit se lire pareil quel que soit l'hôte. Lu dans
        // une culture à virgule décimale, il deviendrait 8 — soit huit fois les frais de
        // livraison versés à chaque course.
        if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var share))
        {
            throw new InvalidOperationException(
                $"PART DU COURSIER ABSENTE OU ILLISIBLE ({CourierPayRule.ShareKey} = « {raw} »).\n\n" +
                "Ce service calcule la rémunération d'une course comme une part des frais de livraison " +
                "encaissés. Aucune valeur par défaut n'est codée : ce serait une décision commerciale " +
                "prise par accident.\n\n" +
                "Renseignez Dispatch:CourierShare (variable Dispatch__CourierShare) avec un nombre " +
                "strictement positif et au plus 1, en notation à point décimal — par exemple 0.80.");
        }

        // Le constructeur porte la validation de l'intervalle : une seule règle, un seul
        // endroit où elle est écrite.
        return new CourierPayRule(share);
    }
}
