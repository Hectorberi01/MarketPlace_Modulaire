using FluentAssertions;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways.Simulation;
using Xunit;

namespace Payments.UnitTests;

public class MobileMoneyGatewayTests
{
    private static MtnMomoPaymentGateway Mtn() => new(new MtnMomoOptions());
    private static MoovPaymentGateway Moov() => new(new MoovOptions());

    [Fact]
    public void Les_psp_mobile_money_exigent_le_numero_du_payeur()
    {
        Mtn().RequiresPayerPhone.Should().BeTrue();
        Moov().RequiresPayerPhone.Should().BeTrue();
    }

    [Fact]
    public async Task Le_resolveur_trouve_momo_et_moov()
    {
        var resolver = new PaymentGatewayResolver(new IPaymentGateway[]
        {
            new StripePaymentGateway(new StripeOptions()),
            Mtn(),
            Moov()
        });

        (await resolver.ResolveAsync("mtnmomo")).IsSuccess.Should().BeTrue();
        (await resolver.ResolveAsync("MOOV")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RequestToPay_renvoie_une_reference_sans_redirection_ni_secret()
    {
        var context = new GatewayChargeContext(Guid.NewGuid(), Guid.NewGuid(), 5000m, "XOF", null, null, PayerMsisdn: "+22997000000");

        var session = await Mtn().CreatePaymentIntentAsync(context);

        session.ProviderReference.Should().StartWith("MTN-");
        session.RedirectUrl.Should().BeNull();
        session.ClientSecret.Should().BeNull();
    }

    [Theory]
    [InlineData("SUCCESSFUL", GatewayOutcome.Captured)]
    [InlineData("FAILED", GatewayOutcome.Failed)]
    [InlineData("TIMEOUT", GatewayOutcome.Failed)]
    [InlineData("PENDING", GatewayOutcome.Pending)]
    public async Task Le_callback_mappe_le_statut_du_request_to_pay(string status, GatewayOutcome expected)
    {
        var body = $"{{\"status\":\"{status}\",\"referenceId\":\"MTN-abc\"}}";

        var result = await Mtn().ParseWebhookAsync(body, signatureHeader: null);

        result.Verified.Should().BeTrue();
        result.Outcome.Should().Be(expected);
        if (expected is GatewayOutcome.Captured or GatewayOutcome.Failed)
        {
            result.ProviderReference.Should().Be("MTN-abc");
        }
    }

    [Fact]
    public async Task GetStatus_simule_l_approbation_du_payeur()
    {
        var result = await Moov().GetStatusAsync("MOOV-xyz");

        result.Verified.Should().BeTrue();
        result.Outcome.Should().Be(GatewayOutcome.Captured);
    }
}
