using HbaExpress.Dispatch.Domain.Couriers;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HbaExpress.Dispatch.Application.Couriers;

/// <summary>
/// Ce qu'il faut pour faire AVANCER une course.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UN SEUL ENDROIT, PARCE QU'IL Y A SIX CHEMINS.
///
/// Accepter, démarrer le ramassage, marquer un colis pris, partir en livraison, livrer :
/// cinq transitions, plus l'acceptation. Recopier le contrôle sur chacune garantit qu'on
/// en oubliera un — et l'oubli ne se verrait pas, puisqu'il produit une route qui marche.
///
/// DEUX TRANSITIONS EN SONT DÉLIBÉRÉMENT EXEMPTÉES : signaler un incident de ramassage et
/// déclarer l'échec d'une course. Ce sont des ISSUES DE SECOURS. Un coursier dont le GPS
/// vient de lâcher, ou qui est dans un sous-sol, doit pouvoir dire « la boutique est
/// fermée » ou « je ne peux pas livrer » — le bloquer là-dessus le laisserait coincé sur
/// une course qu'il ne peut ni terminer ni abandonner, et la course expirerait sans que
/// personne ne sache pourquoi.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class CourierActionPolicy
{
    /// <summary>
    /// Au-delà, la position est trop vieille pour qu'on croie le coursier sur place.
    ///
    /// CINQ MINUTES, ET NON LES DEUX DE L'ÉLIGIBILITÉ AUX OFFRES. Les deux règles ne
    /// répondent pas à la même question. Pour offrir une course, on veut savoir où il est
    /// MAINTENANT afin de mesurer une distance : deux minutes, sinon le calcul est faux.
    /// Pour agir sur une course qu'il a déjà, on veut seulement savoir qu'il est en train
    /// de rouler : une coupure réseau de trois minutes sous un pont ne doit pas
    /// l'empêcher de valider un ramassage.
    ///
    /// Trop court, on bloque un coursier honnête. Trop long, la règle ne dit plus rien.
    /// </summary>
    public static readonly TimeSpan PositionMaxAge = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Le coursier peut-il agir ? Sinon, une erreur qui dit à l'application QUOI FAIRE.
    ///
    /// Le message compte : « position trop ancienne » sans plus laisserait l'application
    /// afficher une erreur et s'arrêter là. Il faut qu'elle sache qu'un envoi de position
    /// débloque la situation — c'est un appel qu'elle fait déjà toutes les quinze secondes.
    /// </summary>
    public static Result Check(Courier? courier, DateTime nowUtc)
    {
        if (courier is null)
        {
            return Result.Failure(Error.NotFound("courier.not_found", "Coursier introuvable."));
        }

        if (courier.Standing != CourierStanding.Active)
        {
            return Result.Failure(Error.Validation(
                "courier.not_active",
                "Ce coursier n'est pas autorisé à travailler."));
        }

        if (courier.CanActOnJob(nowUtc, PositionMaxAge))
        {
            return Result.Success();
        }

        var age = courier.PositionAge(nowUtc);

        return Result.Failure(Error.Validation(
            "courier.position_stale",
            age is null
                ? "Aucune position connue. Envoyez votre position (PUT /api/courier/position) avant d'agir sur une course."
                : $"Position trop ancienne ({(int)age.Value.TotalMinutes} min). Envoyez votre position "
                  + "(PUT /api/courier/position) avant d'agir sur une course."));
    }
}
