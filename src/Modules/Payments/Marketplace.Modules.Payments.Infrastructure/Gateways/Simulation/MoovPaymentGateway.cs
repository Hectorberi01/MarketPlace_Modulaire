namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Simulation;

/// <summary>
/// Adaptateur Moov Money (stub sandbox). Même logique RequestToPay + callback de
/// statut que MTN. Pour le réel : API Moov (MerchantId / ApiKey), corrélation par
/// transaction id.
/// </summary>
public sealed class MoovPaymentGateway : MobileMoneyPaymentGateway
{
    private readonly MoovOptions _options;

    public MoovPaymentGateway(MoovOptions options) => _options = options;

    public override string Provider => "Moov";

    protected override string ReferencePrefix => "MOOV";
    protected override string WebhookSecret => _options.WebhookSecret;
}
