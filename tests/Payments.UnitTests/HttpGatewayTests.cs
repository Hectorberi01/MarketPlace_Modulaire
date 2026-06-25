using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways.Real;
using Xunit;

namespace Payments.UnitTests;

public class HttpGatewayTests
{
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
        // Secret vide => sandbox permissif.
        GatewayWebhook.VerifySignature(body, null, string.Empty).Should().BeTrue();
    }
}
