using FluentAssertions;
using Marketplace.Modules.Delivery.Domain.Couriers;
using Xunit;

namespace Delivery.UnitTests;

/// <summary>
/// Le coursier vu par la marketplace : un bénéficiaire de virement, et le droit — ou
/// non — d'en recevoir un.
/// </summary>
public class CourierTests
{
    // Numéro béninois au format POST-2024 : dix chiffres. Les anciens numéros à huit
    // chiffres sont refusés par BeninGeography, et c'est testé plus bas.
    private static Courier NewCourier(string phone = "01 97 12 34 56")
        => Courier.Register(
            Guid.NewGuid(), "Kossi Adjovi", phone,
            payoutProvider: "MTNMOMO", payoutAccountNumber: "+229 01 97 12 34 56").Value;

    [Fact]
    public void Un_coursier_enregistre_n_est_PAS_encore_payable()
    {
        var courier = NewCourier();

        // La vérification n'a pas eu lieu : payer maintenant reviendrait à virer de
        // l'argent à quelqu'un dont on n'a pas vérifié l'identité.
        courier.Status.Should().Be(CourierStatus.Pending);
        courier.CanBePaid.Should().BeFalse();
    }

    [Fact]
    public void Le_numero_de_versement_est_reduit_a_ses_chiffres()
    {
        var courier = NewCourier();

        // Un numéro saisi avec des espaces et un indicatif doit désigner le même compte
        // que le même numéro saisi brut — sinon le doublon se découvre au premier
        // versement raté.
        courier.Payout.AccountNumber.Should().Be("2290197123456");
        courier.Payout.Provider.Should().Be("mtnmomo", "l'opérateur est normalisé en minuscules");
    }

    [Fact]
    public void Le_telephone_est_normalise_au_format_international()
    {
        var courier = NewCourier("+229 01 97 12 34 56");

        // Le même coursier saisi « 0197123456 » ou « +229 01 97 12 34 56 » doit donner
        // le même numéro stocké : c'est ce qui rend le rapprochement possible quand un
        // versement échoue et qu'il faut le joindre.
        courier.Phone.Should().Be("+2290197123456");
    }

    [Fact]
    public void Un_ancien_numero_a_huit_chiffres_refuse_l_enregistrement()
    {
        // Numéro d'avant la migration béninoise de 2024 : il n'aboutit plus. L'accepter
        // en silence, c'est enregistrer un coursier qu'on ne pourra jamais joindre le
        // jour où son versement échoue.
        var refused = Courier.Register(
            Guid.NewGuid(), "Kossi Adjovi", "97123456", "MTNMOMO", "0197123456");

        refused.IsFailure.Should().BeTrue();
        refused.Error.Code.Should().Be("delivery.courier.phone_invalid");
    }

    [Fact]
    public void Un_compte_de_versement_trop_court_refuse_l_enregistrement()
    {
        var refused = Courier.Register(
            Guid.NewGuid(), "Kossi Adjovi", "0197123456", "MTNMOMO", "123");

        refused.IsFailure.Should().BeTrue();
        refused.Error.Code.Should().Be("delivery.courier.payout_account_invalid");
    }

    [Fact]
    public void L_activation_rend_le_coursier_payable()
    {
        var courier = NewCourier();

        courier.Activate().IsSuccess.Should().BeTrue();

        courier.CanBePaid.Should().BeTrue();
        courier.Status.Should().Be(CourierStatus.Active);
    }

    [Fact]
    public void Suspendre_coupe_les_NOUVEAUX_credits_sans_toucher_a_ce_qui_est_acquis()
    {
        var courier = NewCourier();
        courier.Activate();

        courier.Suspend("Colis non remis").IsSuccess.Should().BeTrue();

        courier.CanBePaid.Should().BeFalse("aucune nouvelle course ne lui est payée");
        courier.SuspensionReason.Should().Be("Colis non remis");

        // ═══════════════════════════════════════════════════════════════════════
        // CE QUE CE TEST PROTÈGE : le compte de versement SURVIT à la suspension.
        //
        // Suspendre n'est pas confisquer. Ce que le coursier a déjà livré, il l'a
        // gagné, et son solde doit rester retirable — c'est pourquoi le résolveur de
        // versement de Settlement ne regarde délibérément pas le statut. Effacer ses
        // coordonnées ici rendrait cette dette impossible à solder.
        // ═══════════════════════════════════════════════════════════════════════
        courier.Payout.Should().NotBeNull();
        courier.Payout.AccountNumber.Should().Be("2290197123456");
    }

    [Fact]
    public void Reactiver_efface_le_motif_de_suspension()
    {
        var courier = NewCourier();
        courier.Suspend("Erreur de modération");

        courier.Activate();

        courier.SuspensionReason.Should().BeNull("un coursier réactivé ne traîne pas son ancien motif");
        courier.CanBePaid.Should().BeTrue();
    }

    [Fact]
    public void Changer_de_compte_de_versement_remplace_les_coordonnees()
    {
        var courier = NewCourier();

        courier.ChangePayoutAccount("moovmoney", "01 96 00 11 22", "K. ADJOVI").IsSuccess.Should().BeTrue();

        courier.Payout.Provider.Should().Be("moovmoney");
        courier.Payout.AccountNumber.Should().Be("0196001122");
        courier.Payout.AccountName.Should().Be("K. ADJOVI");
    }
}
