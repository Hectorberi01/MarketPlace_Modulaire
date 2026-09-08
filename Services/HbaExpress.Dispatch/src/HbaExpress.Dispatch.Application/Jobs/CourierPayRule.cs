using HbaExpress.Dispatch.Domain.Jobs;

namespace HbaExpress.Dispatch.Application.Jobs;

/// <summary>
/// Ce qu'une course rapporte au coursier.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE PART DES FRAIS DE LIVRAISON, ET CETTE PART EST UN RÉGLAGE OBLIGATOIRE.
///
/// La règle définitive — base, kilométrage, supplément par point de ramassage, heure —
/// n'est pas arrêtée (CDC §5.3). En attendant, le coursier touche une PART des frais de
/// livraison encaissés sur la commande, et la plateforme garde le reste.
///
/// AUCUNE VALEUR PAR DÉFAUT N'EST CODÉE ICI, ET C'EST LE POINT. Un défaut à 0,8 serait
/// une décision commerciale prise par accident, invisible, et qu'on découvrirait au
/// premier relevé. Un défaut à 1,0 ferait travailler la plateforme à perte. La part doit
/// donc être écrite dans la configuration, et le service REFUSE DE DÉMARRER sans elle :
/// la décision est forcée au grand jour plutôt que dissimulée dans le code.
///
/// LE MONTANT EST ARRONDI VERS LE BAS. La marketplace refuse toute rémunération
/// supérieure aux frais de livraison de la commande ; un arrondi au supérieur pourrait,
/// à part 1,0 et sur un centime, faire rejeter un paiement pourtant légitime. Arrondir
/// vers le bas ne peut jamais produire ce cas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CourierPayRule
{
    /// <summary>Emplacement de la part dans la configuration. Entre 0 (exclu) et 1 (inclus).</summary>
    public const string ShareKey = "Dispatch:CourierShare";

    public CourierPayRule(decimal share)
    {
        if (share <= 0m || share > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(share), share,
                $"La part du coursier ({ShareKey}) doit être strictement positive et au plus 1. " +
                "Au-delà de 1, la marketplace rejetterait chaque paiement — elle refuse toute " +
                "rémunération supérieure aux frais de livraison encaissés.");
        }

        Share = share;
    }

    public decimal Share { get; }

    /// <summary>Rémunération due pour cette course, arrondie au centime INFÉRIEUR.</summary>
    public decimal AmountFor(DeliveryJob job)
    {
        var exact = job.ShippingFee * Share;
        return Math.Floor(exact * 100m) / 100m;
    }
}
