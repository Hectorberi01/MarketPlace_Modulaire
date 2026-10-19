using System.Text.Json;
using FluentAssertions;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways.Real;
using Marketplace.Modules.Payments.Infrastructure.Gateways.Simulation;
using Xunit;

namespace Payments.UnitTests;

/// <summary>
/// KkiaPay en flux widget : aucun appel réseau, et un rappel vérifié par comparaison
/// du secret (pas de HMAC). Les tests posent le drapeau permissif à FALSE — le défaut
/// de production — d'où la collection sérialisée, cet état étant statique.
/// </summary>
[Collection("GatewayWebhook state")]
public class KkiapayGatewayTests : IDisposable
{
    private const string Secret = "kkia_secret_de_test";

    private readonly bool _previousAllowUnsigned = GatewayWebhook.AllowUnsignedWhenSecretMissing;

    public KkiapayGatewayTests() => GatewayWebhook.AllowUnsignedWhenSecretMissing = false;

    public void Dispose() => GatewayWebhook.AllowUnsignedWhenSecretMissing = _previousAllowUnsigned;

    private static KkiapayWidgetGateway Kkiapay(string webhookSecret = Secret)
        => new(new KkiapayOptions { PublicKey = "pk_test", WebhookSecret = webhookSecret });

    private static GatewayChargeContext Commande(Guid paiement, decimal montant = 15000m, string devise = "XOF")
        => new(paiement, Guid.NewGuid(), montant, devise, "https://boutique/retour", null, "22997000000");

    [Fact]
    public async Task Aucune_page_hebergee_donc_le_checkout_est_refuse()
    {
        Func<Task> acte = () => Kkiapay().CreateCheckoutAsync(Commande(Guid.NewGuid()));

        await acte.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Lintention_rend_les_parametres_du_widget_et_notre_reference()
    {
        var paiement = Guid.NewGuid();

        var session = await Kkiapay().CreatePaymentIntentAsync(Commande(paiement));

        session.ProviderReference.Should().Be($"kkia_{paiement:N}");
        session.RedirectUrl.Should().BeNull();
        session.ClientSecret.Should().NotBeNullOrWhiteSpace();

        using var parametres = JsonDocument.Parse(session.ClientSecret!);
        var racine = parametres.RootElement;
        racine.GetProperty("publicKey").GetString().Should().Be("pk_test");
        racine.GetProperty("amount").GetInt64().Should().Be(15000);
        racine.GetProperty("currency").GetString().Should().Be("XOF");
        racine.GetProperty("sandbox").GetBoolean().Should().BeTrue();

        // LA CORRÉLATION TIENT À CE CHAMP : KkiaPay le rend dans « stateData », et
        // c'est le seul lien entre son rappel et notre paiement.
        racine.GetProperty("data").GetString().Should().Be(session.ProviderReference);
    }

    [Fact]
    public async Task Une_devise_que_le_compte_marchand_ne_traite_pas_refuse_lencaissement()
    {
        Func<Task> acte = () => Kkiapay().CreatePaymentIntentAsync(Commande(Guid.NewGuid(), 50m, "EUR"));

        await acte.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Le_rappel_portant_le_bon_secret_encaisse_et_rend_notre_reference()
    {
        var corps = """
            {"event":"transaction.success","isPaymentSucces":true,"transactionId":"TRX-1","stateData":"kkia_abc"}
            """;

        var evenement = await Kkiapay().ParseWebhookAsync(corps, Secret);

        evenement.Verified.Should().BeTrue();
        evenement.Outcome.Should().Be(GatewayOutcome.Captured);
        evenement.ProviderReference.Should().Be("kkia_abc");
    }

    [Fact]
    public async Task Un_mauvais_secret_ne_vaut_pas_signature()
    {
        var evenement = await Kkiapay().ParseWebhookAsync(
            """{"event":"transaction.success","stateData":"kkia_abc"}""", "mauvais_secret");

        evenement.Verified.Should().BeFalse();
        evenement.Outcome.Should().Be(GatewayOutcome.Ignored);
    }

    [Fact]
    public async Task Sans_secret_configure_le_rappel_est_rejete_hors_development()
    {
        var evenement = await Kkiapay(webhookSecret: string.Empty).ParseWebhookAsync(
            """{"event":"transaction.success","stateData":"kkia_abc"}""", signatureHeader: null);

        evenement.Verified.Should().BeFalse();
    }

    [Fact]
    public async Task Lechec_porte_le_message_du_prestataire()
    {
        var corps = """
            {"event":"transaction.failed","failureCode":"INSUFFICIENT_FUNDS","failureMessage":"Solde insuffisant","stateData":"kkia_abc"}
            """;

        var evenement = await Kkiapay().ParseWebhookAsync(corps, Secret);

        evenement.Outcome.Should().Be(GatewayOutcome.Failed);
        evenement.FailureReason.Should().Be("Solde insuffisant (INSUFFICIENT_FUNDS)");
    }

    [Theory]
    [InlineData(true, GatewayOutcome.Captured)]
    [InlineData(false, GatewayOutcome.Failed)]
    public async Task Sans_champ_event_cest_isPaymentSucces_qui_fait_foi(bool reussi, GatewayOutcome attendu)
    {
        var corps = $"{{\"isPaymentSucces\":{reussi.ToString().ToLowerInvariant()},\"stateData\":\"kkia_abc\"}}";

        var evenement = await Kkiapay().ParseWebhookAsync(corps, Secret);

        evenement.Outcome.Should().Be(attendu);
    }

    [Fact]
    public async Task Un_evenement_inconnu_est_acquitte_sans_rien_appliquer()
    {
        var evenement = await Kkiapay().ParseWebhookAsync(
            """{"event":"transaction.whatever","stateData":"kkia_abc"}""", Secret);

        evenement.Verified.Should().BeTrue();
        evenement.Outcome.Should().Be(GatewayOutcome.Ignored);
    }

    [Fact]
    public async Task Le_statut_reste_en_attente_faute_dendpoint_dinterrogation()
    {
        var evenement = await Kkiapay().GetStatusAsync("kkia_abc");

        evenement.Outcome.Should().Be(GatewayOutcome.Pending);
    }

    [Fact]
    public async Task Le_remboursement_echoue_explicitement_plutot_que_de_pretendre()
    {
        var resultat = await Kkiapay().RefundAsync("kkia_abc");

        resultat.Success.Should().BeFalse();
        resultat.Error.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Le stub ne diffère QUE sur ces deux points : c'est ce qui rend l'écart
    /// vérifiable au lieu d'être une seconde implémentation à relire.
    /// </summary>
    [Fact]
    public async Task Le_stub_conclut_la_session_et_accepte_le_remboursement()
    {
        var stub = new KkiapaySimulatedGateway(new KkiapayOptions());

        (await stub.GetStatusAsync("kkia_abc")).Outcome.Should().Be(GatewayOutcome.Captured);
        (await stub.RefundAsync("kkia_abc")).Success.Should().BeTrue();
    }
}
