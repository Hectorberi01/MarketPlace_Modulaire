using FluentAssertions;
using System.Linq;
using Marketplace.Modules.Disputes.Domain.Disputes;
using Xunit;

namespace Disputes.UnitTests;

public class DisputeAggregateTests
{
    private static Dispute NewDispute()
        => Dispute.Open(Guid.NewGuid(), Guid.NewGuid(), DisputeType.NotReceived, "Je n'ai jamais reçu le colis.", null).Value;

    [Fact]
    public void Opened_dispute_has_initial_message_and_is_open()
    {
        var dispute = NewDispute();

        dispute.Status.Should().Be(DisputeStatus.Open);
        dispute.Messages.Should().HaveCount(1);
    }

    [Fact]
    public void Review_then_resolve_sets_resolution()
    {
        var dispute = NewDispute();

        dispute.StartReview().IsSuccess.Should().BeTrue();
        dispute.Status.Should().Be(DisputeStatus.UnderReview);

        dispute.Resolve(DisputeResolution.RefundBuyer).IsSuccess.Should().BeTrue();
        dispute.Status.Should().Be(DisputeStatus.Resolved);
        dispute.Resolution.Should().Be(DisputeResolution.RefundBuyer);
    }

    [Fact]
    public void Resolved_dispute_rejects_new_messages()
    {
        var dispute = NewDispute();
        dispute.Resolve(DisputeResolution.ReleaseToSeller);

        dispute.AddMessage(Guid.NewGuid(), "encore un mot", null).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Open_without_message_fails()
        => Dispute.Open(Guid.NewGuid(), Guid.NewGuid(), DisputeType.Other, "  ", null).IsFailure.Should().BeTrue();

    // ═════════════════════════════════════════════════════════════════════════════
    // LA PREUVE PHOTO DEVIENT UN LIEN CLIQUABLE DANS LA CONSOLE D'ADMINISTRATION.
    //
    // Elle n'était vérifiée nulle part : un client ou un vendeur authentifié pouvait
    // déposer « javascript:… », et l'écran Modération le rendait dans un `href`. La
    // CSP de la console ne l'aurait pas bloqué — son `script-src` porte
    // 'unsafe-inline', qui autorise ces URL — et le script se serait exécuté dans une
    // origine où `/api/bff/*` est un proxy déjà authentifié.
    //
    // Ces cas sont écrits par SCHÉMA et non par liste de charges utiles : ce n'est pas
    // « javascript » qu'on refuse, c'est tout ce qui n'est pas https. Une liste noire
    // aurait manqué le suivant.
    // ═════════════════════════════════════════════════════════════════════════════
    [Theory]
    [InlineData("javascript:fetch('/api/bff/admin/sellers')")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("http://exemple.test/preuve.jpg")]
    [InlineData("/preuve.jpg")]
    [InlineData("preuve.jpg")]
    public void Une_preuve_qui_n_est_pas_https_est_refusee(string preuve)
    {
        Dispute.Open(Guid.NewGuid(), Guid.NewGuid(), DisputeType.DamagedItem, "Colis abîmé.", preuve)
            .IsFailure.Should().BeTrue($"« {preuve} » n'est pas une adresse https et finit dans un href de la console");

        var dispute = NewDispute();
        dispute.AddMessage(Guid.NewGuid(), "voici la preuve", preuve)
            .IsFailure.Should().BeTrue("les deux chemins portent la même valeur, donc le même garde");
    }

    [Fact]
    public void Une_preuve_https_est_acceptee_et_conservee()
    {
        const string preuve = "https://images.hbamediacore.fr/litiges/abc.jpg";

        var dispute = Dispute.Open(Guid.NewGuid(), Guid.NewGuid(), DisputeType.DamagedItem, "Colis abîmé.", preuve);

        dispute.IsSuccess.Should().BeTrue();
        dispute.Value.Messages.Single().PhotoUrl.Should().Be(preuve);
    }

    [Fact]
    public void Une_preuve_absente_reste_permise()
    {
        // La pièce jointe est facultative : la plupart des litiges n'en portent pas.
        // Sans ce cas, le garde pourrait devenir une exigence sans que rien ne le dise.
        Dispute.Open(Guid.NewGuid(), Guid.NewGuid(), DisputeType.Other, "Rien à joindre.", null)
            .IsSuccess.Should().BeTrue();

        NewDispute().AddMessage(Guid.NewGuid(), "toujours rien", "   ")
            .IsSuccess.Should().BeTrue("une chaîne vide vaut « pas de preuve », pas « preuve invalide »");
    }

    [Fact]
    public void Une_preuve_demesuree_est_refusee()
    {
        // Une URL de deux kilo-octets n'est pas une adresse d'image, c'est une charge
        // utile qu'on éviterait de stocker puis de relire.
        var trop = "https://exemple.test/" + new string('a', 2100);

        Dispute.Open(Guid.NewGuid(), Guid.NewGuid(), DisputeType.DamagedItem, "Colis abîmé.", trop)
            .IsFailure.Should().BeTrue();
    }
}
