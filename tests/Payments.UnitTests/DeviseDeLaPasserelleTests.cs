using System.Net.Http;
using FluentAssertions;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways.Real;
using Xunit;

namespace Payments.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// LA DEVISE ENVOYÉE AU PRESTATAIRE EST CELLE DE LA COMMANDE, OU RIEN.
///
/// FedaPay, MTN MoMo et Moov envoyaient la devise lue dans la CONFIGURATION avec le
/// montant lu dans la COMMANDE. Les deux ne se rencontraient nulle part. Une commande
/// de 50,00 EUR partait donc comme une charge de 50 XOF : la valeur numérique de la
/// commande, l'étiquette de la configuration. L'acheteur payait environ un
/// trente-troisième du dû, et le paiement était ensuite marqué ENCAISSÉ pour le
/// montant complet.
///
/// Le correctif ne se contente pas de transmettre la bonne devise : il REFUSE une
/// devise que la passerelle ne sait pas traiter. Accepter en silence était précisément
/// le défaut, et une conversion inventée en aurait été un second — quel taux, à quelle
/// date, opposable à qui ? Ces tests gardent le refus, pas seulement la transmission.
///
/// La fabrique de clients HTTP lève : c'est la démonstration que le refus est décidé
/// AVANT tout appel réseau. Rien n'est créé chez le prestataire, pas même un jeton.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DeviseDeLaPasserelleTests
{
    private sealed class FabriqueQuiInterditLeReseau : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException(
                "Appel réseau tenté alors que la devise aurait dû être refusée en amont.");
    }

    private static GatewayChargeContext Commande(decimal montant, string devise)
        => new(Guid.NewGuid(), Guid.NewGuid(), montant, devise, null, null, PayerMsisdn: "+22997000000");

    // ── Le refus ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FedaPay_refuse_une_commande_libellee_dans_une_autre_devise()
    {
        var passerelle = new FedaPayHttpGateway(
            new FabriqueQuiInterditLeReseau(), new FedaPayOptions { ApiKey = "sk_sandbox_x", Currency = "XOF" });

        Func<Task> acte = () => passerelle.CreateCheckoutAsync(Commande(50m, "EUR"));

        // Le message doit nommer LES DEUX devises : un refus qu'on ne peut pas
        // diagnostiquer se termine en « on remet la config d'avant ».
        (await acte.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("EUR").And.Contain("XOF");
    }

    [Fact]
    public async Task MtnMomo_refuse_avant_meme_de_demander_un_jeton()
    {
        var passerelle = new MtnMomoHttpGateway(
            new FabriqueQuiInterditLeReseau(), new MtnMomoOptions { Currency = "XOF" });

        Func<Task> acte = () => passerelle.CreatePaymentIntentAsync(Commande(50m, "EUR"));

        (await acte.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("EUR").And.Contain("XOF");
    }

    [Fact]
    public async Task Moov_refuse_avant_meme_de_demander_un_jeton()
    {
        var passerelle = new MoovHttpGateway(
            new FabriqueQuiInterditLeReseau(), new MoovOptions { Currency = "XOF" });

        Func<Task> acte = () => passerelle.CreateCheckoutAsync(Commande(50m, "EUR"));

        (await acte.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("EUR").And.Contain("XOF");
    }

    [Fact]
    public async Task Une_passerelle_sans_devise_configuree_ne_devine_pas_le_franc_cfa()
    {
        // Une devise implicite est exactement ce qui a produit le défaut d'origine :
        // on refuse plutôt que de supposer que le compte marchand est en XOF.
        var passerelle = new MoovHttpGateway(
            new FabriqueQuiInterditLeReseau(), new MoovOptions { Currency = string.Empty });

        Func<Task> acte = () => passerelle.CreateCheckoutAsync(Commande(5_000m, "XOF"));

        await acte.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── La troncature à l'entier ────────────────────────────────────────────────

    [Fact]
    public async Task FedaPay_refuse_une_devise_a_centimes_plutot_que_de_tronquer()
    {
        // Le champ « amount » de FedaPay est un ENTIER. Configurer la passerelle en
        // euro et lui envoyer 50,75 aurait fait encaisser 51 — la seconde moitié du
        // défaut : la commande enregistrée à un montant, le prestataire à un autre.
        var passerelle = new FedaPayHttpGateway(
            new FabriqueQuiInterditLeReseau(), new FedaPayOptions { ApiKey = "sk_sandbox_x", Currency = "EUR" });

        Func<Task> acte = () => passerelle.CreateCheckoutAsync(Commande(50.75m, "EUR"));

        (await acte.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("EUR");
    }
}
