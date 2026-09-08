using FluentAssertions;
using HbaExpress.Dispatch.Application.Jobs;
using HbaExpress.Dispatch.Domain.Jobs;
using HbaExpress.Dispatch.Domain.Matching;
using Xunit;

namespace HbaExpress.Dispatch.UnitTests;

/// <summary>
/// Ce que devient une course que personne n'accepte.
///
/// Sans ce balayage, la réponse est « rien » : les offres expirent, la course garde son
/// statut, aucune nouvelle vague ne part, et rien ne le signale. Pas d'erreur, pas de
/// journal — juste un acheteur qui attend une livraison que personne ne viendra faire.
/// </summary>
public class RedispatchServiceTests
{
    [Fact]
    public void Le_rayon_double_a_chaque_vague()
    {
        var first = RedispatchService.WidenFor(0);
        var second = RedispatchService.WidenFor(1);
        var third = RedispatchService.WidenFor(2);

        first.RadiusKm.Should().Be(DispatchPolicy.FirstRound.RadiusKm);
        second.RadiusKm.Should().Be(first.RadiusKm * 2);
        third.RadiusKm.Should().Be(first.RadiusKm * 4);

        // On sollicite aussi plus de monde à chaque tour : élargir le rayon sans élargir
        // le nombre d'appelés ne ferait que déplacer le cercle, pas l'agrandir.
        third.MaxCouriers.Should().BeGreaterThan(first.MaxCouriers);
    }

    [Fact]
    public void Une_course_jamais_offerte_repart_sur_le_rayon_initial()
    {
        // Cas de la course créée quand personne n'est en ligne : elle n'a fait aucune
        // vague, elle doit donc commencer par le rayon serré — pas par un rayon élargi
        // qu'elle n'a jamais mérité.
        RedispatchService.WidenFor(0).RadiusKm.Should().Be(3d);
    }

    [Fact]
    public void Le_delai_global_laisse_le_temps_de_plusieurs_vagues()
    {
        // Cinq minutes face à des offres de vingt-cinq secondes : une douzaine de vagues.
        // Si personne n'a pris au bout de ça, ce n'est plus une question de rayon.
        var waves = RedispatchService.GlobalDeadline.TotalSeconds / DeliveryJobService.OfferWindow.TotalSeconds;

        waves.Should().BeGreaterThan(10);
    }
}
