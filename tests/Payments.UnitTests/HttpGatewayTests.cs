using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways.Real;
using Xunit;

namespace Payments.UnitTests;

/// <summary>
/// ⚠️ Cette classe manipule <see cref="GatewayWebhook.AllowUnsignedWhenSecretMissing"/>,
/// qui est un état STATIQUE MUTABLE. xUnit exécute les classes de test en parallèle :
/// sans collection dédiée, une autre classe de ce projet (MobileMoneyGatewayTests,
/// PaymentGatewayTests) pourrait lire le drapeau pendant qu'on le bascule, et échouer
/// une fois sur trois sans qu'on comprenne pourquoi.
///
/// La collection sérialise. Ce n'est pas une précaution théorique : un test
/// intermittent sur la vérification de signature d'un webhook de paiement est
/// exactement le genre de rouge qu'on finit par ignorer.
/// </summary>
[Collection("GatewayWebhook state")]
public class HttpGatewayTests : IDisposable
{
    private readonly bool _previousAllowUnsigned = GatewayWebhook.AllowUnsignedWhenSecretMissing;

    /// <summary>
    /// Le défaut de production est FERMÉ (false) : un webhook non signé, alors
    /// qu'aucun secret n'est configuré, est REJETÉ. Sans quoi n'importe qui pourrait
    /// créditer une commande en postant un faux webhook.
    ///
    /// Les tests ci-dessous simulent le bac à sable, où l'hôte (Program.cs) active
    /// explicitement le mode permissif en Development. Ils doivent donc poser le
    /// drapeau eux-mêmes — et le remettre après, d'où le Dispose.
    /// </summary>
    public HttpGatewayTests() => GatewayWebhook.AllowUnsignedWhenSecretMissing = true;

    public void Dispose() => GatewayWebhook.AllowUnsignedWhenSecretMissing = _previousAllowUnsigned;

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static MtnMomoHttpGateway RealMtn(string webhookSecret = "")
        => new(new StubHttpClientFactory(), new MtnMomoOptions { WebhookSecret = webhookSecret });

    private static MoovHttpGateway RealMoov()
        => new(new StubHttpClientFactory(), new MoovOptions());

    private static StripeHttpGateway RealStripe(string webhookSecret = "")
        => new(new StubHttpClientFactory(), new StripeOptions { WebhookSecret = webhookSecret });

    [Fact]
    public async Task Stripe_reel_verifie_la_signature_t_v1_et_mappe_l_evenement()
    {
        const string secret = "whsec_stripe";
        var body = "{\"type\":\"checkout.session.completed\",\"data\":{\"object\":{\"id\":\"cs_real\"}}}";
        var timestamp = "1700000000";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var v1 = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();

        var gateway = RealStripe(secret);

        var valid = await gateway.ParseWebhookAsync(body, $"t={timestamp},v1={v1}");
        valid.Verified.Should().BeTrue();
        valid.Outcome.Should().Be(GatewayOutcome.Captured);
        valid.ProviderReference.Should().Be("cs_real");

        var bad = await gateway.ParseWebhookAsync(body, $"t={timestamp},v1=deadbeef");
        bad.Verified.Should().BeFalse();
    }

    [Fact]
    public async Task Le_callback_moov_reel_mappe_le_statut_et_la_reference()
    {
        var body = "{\"status\":\"SUCCESSFUL\",\"transactionId\":\"MOOV-7\"}";

        var gateway = RealMoov();
        var result = await gateway.ParseWebhookAsync(body, signatureHeader: null);

        gateway.Provider.Should().Be("Moov");
        gateway.RequiresPayerPhone.Should().BeTrue();
        result.Outcome.Should().Be(GatewayOutcome.Captured);
        result.ProviderReference.Should().Be("MOOV-7");
    }

    [Fact]
    public void L_adaptateur_reel_garde_l_identite_et_la_capability()
    {
        var gateway = RealMtn();
        gateway.Provider.Should().Be("MtnMomo");
        gateway.RequiresPayerPhone.Should().BeTrue();
    }

    [Theory]
    [InlineData("SUCCESSFUL", GatewayOutcome.Captured)]
    [InlineData("FAILED", GatewayOutcome.Failed)]
    [InlineData("PENDING", GatewayOutcome.Pending)]
    public async Task Le_callback_reel_mappe_le_statut(string status, GatewayOutcome expected)
    {
        var body = $"{{\"status\":\"{status}\",\"referenceId\":\"MTN-1\"}}";

        var result = await RealMtn().ParseWebhookAsync(body, signatureHeader: null);

        result.Verified.Should().BeTrue();
        result.Outcome.Should().Be(expected);
    }

    [Fact]
    public void Le_helper_de_signature_accepte_le_bon_hmac_et_rejette_le_mauvais()
    {
        const string secret = "whsec_momo";
        var body = "{\"status\":\"SUCCESSFUL\",\"referenceId\":\"MTN-1\"}";

        GatewayWebhook.VerifySignature(body, GatewayWebhook.ComputeSignature(body, secret), secret).Should().BeTrue();
        GatewayWebhook.VerifySignature(body, "mauvaise", secret).Should().BeFalse();

        // Secret vide + mode permissif (posé par le constructeur) => accepté.
        // C'est le cas du bac à sable, pas celui de la production.
        GatewayWebhook.VerifySignature(body, null, string.Empty).Should().BeTrue();
    }

    /// <summary>
    /// LE test qui compte vraiment, et qui manquait.
    ///
    /// En production, aucun secret n'est parfois configuré — par oubli, par erreur de
    /// vault, par variable d'environnement mal nommée. Ce jour-là, le webhook DOIT
    /// être rejeté. Un fail-open silencieux permettrait à quiconque connaît l'URL de
    /// marquer une commande comme payée en postant un JSON.
    ///
    /// Ce test verrouille le défaut. Si quelqu'un remet un jour la valeur à `true`
    /// « pour débloquer le sandbox », c'est ici que ça cassera.
    /// </summary>
    [Fact]
    public void Sans_secret_ET_sans_mode_permissif_le_webhook_est_REJETE()
    {
        var previous = GatewayWebhook.AllowUnsignedWhenSecretMissing;
        try
        {
            GatewayWebhook.AllowUnsignedWhenSecretMissing = false; // le défaut de production
            var body = "{\"status\":\"SUCCESSFUL\",\"referenceId\":\"MTN-1\"}";

            GatewayWebhook.VerifySignature(body, signatureHeader: null, secret: string.Empty)
                .Should().BeFalse("un webhook non signé sans secret configuré ne doit JAMAIS créditer une commande");
        }
        finally
        {
            GatewayWebhook.AllowUnsignedWhenSecretMissing = previous;
        }
    }
}

/// <summary>
/// Sérialise les classes qui touchent au drapeau statique de GatewayWebhook.
/// </summary>
[CollectionDefinition("GatewayWebhook state", DisableParallelization = true)]
public sealed class GatewayWebhookCollection;
