using HBA.Deliveries.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace HBA.Deliveries.Infrastructure.Configuration;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA PART DU LIVREUR, LUE DANS LA CONFIGURATION.
///
/// Clé : <c>Delivery:DriverSharePercent</c>, en POURCENTAGE (70 pour 70 %) et non
/// en fraction. C'est ce qu'écrira un humain dans un fichier de configuration ou
/// une variable d'environnement, et « 0.7 » saisi comme « 7 » se serait vu ; saisi
/// comme « 0,7 » dans une locale francophone, il aurait été relu comme 7.
///
/// ⚠️ CE DÉFAUT DE 70 % N'A PAS ÉTÉ VALIDÉ COMMERCIALEMENT.
///
/// Il est plausible pour de la livraison à la demande, et il est journalisé au
/// démarrage précisément pour que personne ne le découvre sur un décompte de
/// livreur. Une valeur par défaut silencieuse sur un partage de recette serait
/// exactement le genre de réglage qu'on retrouve six mois plus tard, inchangé,
/// dans une réclamation.
///
/// POURQUOI ON REFUSE DE DÉMARRER SUR UNE VALEUR ABERRANTE
///
/// Un « 70 » saisi « 700 » donnerait sept fois le prix de la course au livreur, sur
/// chaque course, sans qu'aucune alerte ne se déclenche — les montants resteraient
/// des nombres parfaitement valides. Une plage refusée à l'amorçage coûte un
/// redémarrage ; le même réglage accepté coûte une campagne de recouvrement.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DeliveryPayoutSettings : IDeliveryPayoutSettings
{
    public const string SectionKey = "Delivery:DriverSharePercent";

    /// <summary>Part retenue à défaut de réglage. À confirmer commercialement.</summary>
    public const decimal DefaultSharePercent = 70m;

    public DeliveryPayoutSettings(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var raw = configuration[SectionKey];

        if (string.IsNullOrWhiteSpace(raw))
        {
            DriverShareRate = DefaultSharePercent / 100m;
            UsesDefault = true;
            return;
        }

        // Culture INVARIANTE, explicitement. Le projet active
        // InvariantGlobalization, mais s'appuyer sur un réglage global pour lire un
        // taux de partage de recette serait fragile : le jour où quelqu'un le
        // désactive, « 67.5 » deviendrait illisible dans une locale francophone et
        // l'on retomberait silencieusement sur le défaut.
        if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var percent))
        {
            throw new InvalidOperationException(
                $"« {SectionKey} » vaut « {raw} », qui n'est pas un nombre. Attendu : un pourcentage, "
                + "par exemple « 70 » pour 70 %.");
        }

        if (percent is < 0m or > 100m)
        {
            throw new InvalidOperationException(
                $"« {SectionKey} » vaut {percent}. Un pourcentage de partage se situe entre 0 et 100 — "
                + "au-delà, le livreur toucherait plus que le prix de la course.");
        }

        DriverShareRate = percent / 100m;
        UsesDefault = false;
    }

    public decimal DriverShareRate { get; }

    /// <summary>Vrai si aucun réglage n'a été fourni. Journalisé au démarrage.</summary>
    public bool UsesDefault { get; }
}
