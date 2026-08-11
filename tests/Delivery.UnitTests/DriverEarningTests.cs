using FluentAssertions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Deliveries.Events;
using HBA.Deliveries.Domain.Drivers;
using Xunit;
using Course = HBA.Deliveries.Domain.Deliveries.Delivery;

namespace Delivery.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA PART DU LIVREUR.
///
/// C'est de l'argent versé à une personne réelle. Une erreur ici ne produit pas un
/// écran cassé mais un décompte faux, découvert par le livreur avant de l'être par
/// nous — et une confiance qui ne revient pas.
///
/// Ces tests portent moins sur l'arithmétique, qui est triviale, que sur les trois
/// décisions qui l'entourent : quand on ne doit RIEN, quand on ne SAIT PAS, et
/// pourquoi le taux est conservé à côté du montant.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public class DriverEarningTests
{
    private const decimal Soixante_dix_pour_cent = 0.70m;

    private static readonly Coordinates Pickup = Coordinates.Create(6.3703, 2.3912).Value;
    private static readonly Coordinates Dropoff = Coordinates.Create(6.3556, 2.4180).Value;

    /// <summary>Une course acceptée, collectée, en transit — prête à être remise.</summary>
    private static Course EnTransit(decimal? price = null)
    {
        var delivery = Course.Create(
            "ORDER-45821", DeliverySource.HbaExpress, DeliveryType.Standard,
            Stop("Boutique", "en face de la pharmacie Sainte-Rita", Pickup),
            Stop("Client", "derrière le marché de Ouando", Dropoff),
            DeliveryPackage.Create("Colis", 2.5m).Value).Value;

        if (price is { } montant)
        {
            delivery.AttachQuote(Guid.NewGuid(), montant, "XOF");
        }

        var driver = DriverId.New();
        delivery.StartSearching();
        delivery.AssignTo(driver);
        delivery.AcceptByDriver(driver);
        delivery.MarkPickedUp();
        delivery.MarkInTransit();

        return delivery;
    }

    private static DeliveryStop Stop(string name, string landmark, Coordinates position)
        => DeliveryStop.Create(name, "+229 01 97 24 18 06", "cotonou", "Fidjrossè",
                               landmark, null, position).Value;

    [Fact]
    public void Le_livreur_touche_sa_part_du_prix_de_la_course()
    {
        // L'exemple du cahier : une course à 2 100 FCFA.
        var delivery = EnTransit(price: 2100m);

        delivery.MarkDelivered(null, Soixante_dix_pour_cent).IsSuccess.Should().BeTrue();

        delivery.DriverEarning.Should().Be(1470m);
        delivery.DriverShareRate.Should().Be(Soixante_dix_pour_cent);
    }

    [Fact]
    public void Le_taux_est_conserve_a_cote_du_montant()
    {
        // ⚠️ CE N'EST PAS DE LA REDONDANCE.
        //
        // Le taux est une décision commerciale qui bougera. Six mois plus tard, sur
        // un gain contesté, il n'existera plus nulle part ailleurs : le réglage
        // courant aura changé. Un montant sans son taux n'est pas une preuve, c'est
        // une affirmation — et c'est le livreur qui perd l'arbitrage.
        var delivery = EnTransit(price: 3000m);
        delivery.MarkDelivered(null, 0.65m);

        delivery.DriverShareRate.Should().Be(0.65m);
        (delivery.Price!.Value * delivery.DriverShareRate!.Value)
            .Should().Be(delivery.DriverEarning!.Value);
    }

    [Fact]
    public void Une_course_sans_prix_ne_produit_AUCUN_gain_et_non_zero()
    {
        // La distinction compte au moment de solder : « aucun gain calculé » se
        // cherche, « zéro franc » se paie. Mettre 0 serait exact arithmétiquement
        // et faux dans les faits — le livreur a bien roulé.
        var delivery = EnTransit(price: null);

        delivery.MarkDelivered(null, Soixante_dix_pour_cent).IsSuccess.Should().BeTrue();

        delivery.DriverEarning.Should().BeNull();
        delivery.DriverShareRate.Should().BeNull();
    }

    [Fact]
    public void Rien_n_est_du_avant_la_remise()
    {
        var delivery = EnTransit(price: 2100m);

        delivery.DriverEarning.Should().BeNull();
        delivery.DriverShareRate.Should().BeNull();
    }

    public static TheoryData<decimal> TauxAberrants => new()
    {
        -0.01m,
        1.01m,
        7.0m,   // « 700 % » saisi pour « 70 % »
    };

    [Theory]
    [MemberData(nameof(TauxAberrants))]
    public void Un_taux_hors_bornes_fait_echouer_la_remise(decimal taux)
    {
        // Sept fois le prix de la course au livreur, sur chaque course, sans
        // qu'aucune alerte ne se déclenche : les montants resteraient des nombres
        // parfaitement valides. La garde est ici EN PLUS de celle de la
        // configuration, parce que le domaine ne doit pas dépendre de la vigilance
        // de ses appelants.
        var delivery = EnTransit(price: 2100m);

        var result = delivery.MarkDelivered(null, taux);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("delivery.share_rate_invalid");

        // Et la course N'EST PAS remise : un taux aberrant ne doit pas clore une
        // course en laissant le gain à corriger après coup.
        delivery.Status.Should().NotBe(DeliveryStatus.Delivered);
    }

    [Fact]
    public void Le_gain_est_arrondi_au_franc_entier()
    {
        // Le XOF n'a pas de subdivision : un montant à décimales ne peut pas être
        // versé par Mobile Money.
        var delivery = EnTransit(price: 1755m);

        delivery.MarkDelivered(null, 0.675m);

        delivery.DriverEarning.Should().Be(1185m); // 1 184,625 → 1 185
        delivery.DriverEarning!.Value.Should().Be(decimal.Truncate(delivery.DriverEarning.Value));
    }

    [Fact]
    public void Un_taux_nul_donne_zero_et_c_est_une_decision_pas_un_oubli()
    {
        // Contrairement à l'absence de prix, un taux de 0 % est un choix explicite
        // de l'appelant — une course offerte, un test. Le montant vaut donc ZÉRO et
        // non « inconnu » : il est calculé, simplement nul.
        var delivery = EnTransit(price: 2100m);

        delivery.MarkDelivered(null, 0m);

        delivery.DriverEarning.Should().Be(0m);
        delivery.DriverShareRate.Should().Be(0m);
    }

    [Fact]
    public void Le_gain_part_dans_l_evenement_de_remise()
    {
        // Le consommateur — paie du livreur, webhook partenaire — ne doit pas
        // refaire le calcul : il paierait les courses anciennes au taux du jour.
        var delivery = EnTransit(price: 2100m);
        delivery.MarkDelivered(null, Soixante_dix_pour_cent);

        var completed = delivery.DomainEvents
            .OfType<DeliveryCompletedDomainEvent>()
            .Single();

        completed.DriverEarning.Should().Be(1470m);
        completed.Currency.Should().Be("XOF");
    }
}
