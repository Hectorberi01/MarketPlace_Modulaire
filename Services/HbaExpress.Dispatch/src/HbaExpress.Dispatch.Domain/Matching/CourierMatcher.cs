using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;

namespace HbaExpress.Dispatch.Domain.Matching;

/// <summary>Réglages d'une vague d'offres.</summary>
/// <param name="RadiusKm">Rayon autour du PREMIER ramassage.</param>
/// <param name="MaxCouriers">Combien de coursiers sollicités simultanément.</param>
/// <param name="PositionMaxAge">Au-delà, la position est trop vieille pour être crue.</param>
public sealed record DispatchPolicy(double RadiusKm, int MaxCouriers, TimeSpan PositionMaxAge)
{
    /// <summary>
    /// Première vague : rayon serré, peu de coursiers.
    ///
    /// TROIS KILOMÈTRES, ET NON DIX. Offrir large maximise les chances qu'un coursier
    /// accepte, mais celui qui accepte de loin met vingt minutes à arriver — le vendeur
    /// attend, l'acheteur attend, et la course coûte plus cher qu'elle ne rapporte. On
    /// commence près, quitte à élargir.
    /// </summary>
    public static DispatchPolicy FirstRound => new(3d, 5, TimeSpan.FromMinutes(2));

    /// <summary>Vague suivante : on élargit plutôt que d'échouer.</summary>
    public DispatchPolicy Widen() => this with { RadiusKm = RadiusKm * 2, MaxCouriers = MaxCouriers + 5 };
}

/// <summary>Un candidat retenu, avec ce qui a motivé son rang.</summary>
public sealed record MatchedCourier(CourierId CourierId, double DistanceKm);

/// <summary>
/// Choisit à QUI offrir une course.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// FONCTION PURE, ET C'EST DÉLIBÉRÉ.
///
/// Le matching est la partie du dispatch qu'on va le plus retoucher : ajouter la note,
/// le taux d'acceptation, l'historique, une préférence de zone. Chaque retouche doit
/// pouvoir être jugée sur des cas précis — « ce coursier-là aurait-il dû être appelé ? » —
/// sans monter une base ni un hub temps réel.
///
/// Elle ne lit donc rien, n'écrit rien, ne connaît ni l'heure ni le réseau : on lui donne
/// des coursiers et une course, elle rend une liste ordonnée.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class CourierMatcher
{
    /// <summary>
    /// Coursiers éligibles pour cette course, du plus proche au plus loin.
    ///
    /// Quatre filtres, dans cet ordre : le droit de travailler et la disponibilité
    /// (avec position fraîche), la ZONE déclarée, puis le RAYON autour du premier
    /// ramassage. Un coursier sans position connue n'est jamais retenu — on ne peut pas
    /// mesurer sa distance, et le supposer proche reviendrait à tirer au sort.
    /// </summary>
    public static IReadOnlyList<MatchedCourier> Match(
        DeliveryJob job,
        IEnumerable<Courier> couriers,
        DispatchPolicy policy,
        DateTime nowUtc,
        ISet<CourierId>? exclude = null)
    {
        var origin = job.FirstPickup;
        if (origin is null)
        {
            return Array.Empty<MatchedCourier>();
        }

        var matched = new List<MatchedCourier>();

        foreach (var courier in couriers)
        {
            if (exclude is not null && exclude.Contains(courier.Id))
            {
                continue;
            }

            if (!courier.CanReceiveOffers(nowUtc, policy.PositionMaxAge))
            {
                continue;
            }

            // La zone prime sur la distance : un coursier peut se trouver à deux
            // kilomètres à vol d'oiseau et de l'autre côté d'une limite qu'il ne
            // dessert pas — la lagune de Cotonou fait exactement cela.
            if (!courier.Serves(origin.CommuneCode))
            {
                continue;
            }

            // Sans position sur le ramassage, on ne peut pas mesurer : on garde le
            // coursier, la zone ayant déjà fait le tri. C'est le cas courant, beaucoup
            // de boutiques n'ayant pas de coordonnées.
            if (origin.Position is null)
            {
                matched.Add(new MatchedCourier(courier.Id, double.MaxValue));
                continue;
            }

            var distance = courier.LastPosition!.DistanceKmTo(origin.Position!);
            if (distance <= policy.RadiusKm)
            {
                matched.Add(new MatchedCourier(courier.Id, distance));
            }
        }

        return matched
            .OrderBy(m => m.DistanceKm)
            .Take(policy.MaxCouriers <= 0 ? 1 : policy.MaxCouriers)
            .ToList();
    }
}
