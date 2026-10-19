using Marketplace.Modules.Payments.Application.Abstractions.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways.Real;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways.Simulation;

/// <summary>
/// Stub sandbox KkiaPay.
///
/// Il DÉRIVE de la passerelle réelle, contrairement aux autres stubs qui héritent de
/// <see cref="SimulatedPaymentGateway"/> : la passerelle KkiaPay ne fait aucun appel
/// réseau, donc préparer le widget et vérifier le rappel se font déjà sans stub. Ne
/// restent ici que les deux écarts de confort du bac à sable, visibles d'un coup
/// d'œil au lieu d'être noyés dans une seconde implémentation.
///
/// RegistryPaymentGatewayResolver interdit de construire cette classe en production.
/// </summary>
public sealed class KkiapaySimulatedGateway : KkiapayWidgetGateway
{
    public KkiapaySimulatedGateway(KkiapayOptions options) : base(options)
    {
    }

    /// <summary>Le retour de redirection conclut la session, faute de rappel en local.</summary>
    public override Task<GatewayEvent> GetStatusAsync(string providerReference, CancellationToken cancellationToken = default)
        => Task.FromResult(new GatewayEvent(Verified: true, GatewayOutcome.Captured, providerReference, null));

    /// <summary>Remboursement accepté sans rien appeler (le réel renvoie un échec).</summary>
    public override Task<GatewayRefundResult> RefundAsync(string providerReference, CancellationToken cancellationToken = default)
        => Task.FromResult(new GatewayRefundResult(Success: true, providerReference, Error: null));
}
