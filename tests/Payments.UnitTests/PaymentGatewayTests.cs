using FluentAssertions;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;
using Marketplace.Modules.Payments.Domain.Payments;
using Marketplace.Modules.Payments.Infrastructure.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways.Simulation;
using Xunit;

namespace Payments.UnitTests;

public class PaymentGatewayTests
{
    private static StripePaymentGateway Stripe(string webhookSecret = "")
        => new(new StripeOptions { WebhookSecret = webhookSecret });

    private static PayPalPaymentGateway PayPal()
        => new(new PayPalOptions());

    [Fact]
    public void Resolver_trouve_les_psp_par_nom_insensible_a_la_casse()
    {
        var resolver = new PaymentGatewayResolver(new IPaymentGateway[] { Stripe(), PayPal() });

        resolver.Resolve("stripe").IsSuccess.Should().BeTrue();
        resolver.Resolve("PAYPAL").IsSuccess.Should().BeTrue();
        resolver.Resolve("MtnMoney").IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Checkout_renvoie_une_url_de_redirection_et_une_reference()
    {
        var session = await Stripe().CreateCheckoutAsync(
            new GatewayChargeContext(Guid.NewGuid(), Guid.NewGuid(), 15000m, "XOF", "https://shop/return", "https://shop/cancel"));

        session.ProviderReference.Should().StartWith("cs_");
        session.RedirectUrl.Should().NotBeNullOrWhiteSpace();
        session.ClientSecret.Should().BeNull();
    }

    [Fact]
    public async Task Intent_renvoie_un_client_secret_sans_redirection()
    {
        var session = await Stripe().CreatePaymentIntentAsync(
            new GatewayChargeContext(Guid.NewGuid(), Guid.NewGuid(), 15000m, "XOF", null, null));

        session.ProviderReference.Should().StartWith("pi_");
        session.ClientSecret.Should().NotBeNullOrWhiteSpace();
        session.RedirectUrl.Should().BeNull();
    }

    [Theory]
    [InlineData("checkout.session.completed", GatewayOutcome.Captured)]
    [InlineData("payment_intent.succeeded", GatewayOutcome.Captured)]
    [InlineData("payment_intent.payment_failed", GatewayOutcome.Failed)]
    [InlineData("charge.refunded", GatewayOutcome.Refunded)]
    [InlineData("invoice.created", GatewayOutcome.Ignored)]
    public async Task Stripe_mappe_les_types_devenement(string type, GatewayOutcome expected)
    {
        var body = $"{{\"type\":\"{type}\",\"providerReference\":\"cs_abc\"}}";

        var result = await Stripe().ParseWebhookAsync(body, signatureHeader: null);

        result.Verified.Should().BeTrue();
        result.Outcome.Should().Be(expected);
        if (expected != GatewayOutcome.Ignored)
        {
            result.ProviderReference.Should().Be("cs_abc");
        }
    }

    [Fact]
    public async Task PayPal_mappe_capture_completed_vers_captured()
    {
        var body = "{\"event_type\":\"PAYMENT.CAPTURE.COMPLETED\",\"resource\":{\"id\":\"ORDER_42\"}}";

        var result = await PayPal().ParseWebhookAsync(body, signatureHeader: null);

        result.Verified.Should().BeTrue();
        result.Outcome.Should().Be(GatewayOutcome.Captured);
        result.ProviderReference.Should().Be("ORDER_42");
    }

    [Fact]
    public async Task Webhook_rejette_une_signature_invalide_quand_un_secret_est_configure()
    {
        var gateway = Stripe(webhookSecret: "whsec_test");
        var body = "{\"type\":\"checkout.session.completed\",\"providerReference\":\"cs_abc\"}";

        var bad = await gateway.ParseWebhookAsync(body, signatureHeader: "mauvaise_signature");
        bad.Verified.Should().BeFalse();

        var valid = await gateway.ParseWebhookAsync(body, SimulatedPaymentGateway.ComputeSignature(body, "whsec_test"));
        valid.Verified.Should().BeTrue();
        valid.Outcome.Should().Be(GatewayOutcome.Captured);
    }

    [Fact]
    public void AttachGatewaySession_rattache_la_reference_quand_en_attente()
    {
        var payment = NewPayment();

        payment.AttachGatewaySession("cs_xyz").IsSuccess.Should().BeTrue();
        payment.ProviderReference.Should().Be("cs_xyz");
    }

    [Fact]
    public void AttachGatewaySession_refuse_apres_encaissement()
    {
        var payment = NewPayment();
        payment.AttachGatewaySession("cs_xyz");
        payment.Capture("cs_xyz");

        payment.AttachGatewaySession("cs_autre").IsFailure.Should().BeTrue();
    }

    private static Payment NewPayment()
        => Payment.Create(Guid.NewGuid(), Guid.NewGuid(), Money.Create(15000m, "XOF").Value, PaymentMethod.Card, "Stripe", PaymentFlow.HostedCheckout).Value;
}
