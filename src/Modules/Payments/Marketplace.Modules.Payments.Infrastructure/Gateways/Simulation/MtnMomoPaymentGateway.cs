namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Simulation;

/// <summary>
/// Adaptateur MTN Mobile Money (Collection API, stub sandbox). RequestToPay +
/// callback de statut. Pour le réel : MoMo Collection (X-Reference-Id, statut
/// SUCCESSFUL/FAILED/PENDING) avec la SubscriptionKey et le couple ApiUser/ApiKey.
/// </summary>
public sealed class MtnMomoPaymentGateway : MobileMoneyPaymentGateway
{
    private readonly MtnMomoOptions _options;

    public MtnMomoPaymentGateway(MtnMomoOptions options) => _options = options;

    public override string Provider => "MtnMomo";

    protected override string ReferencePrefix => "MTN";
    protected override string WebhookSecret => _options.WebhookSecret;
}
