using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Payments.Application.Abstractions.Gateways;

/// <summary>
/// Sélectionne l'implémentation PSP correspondant à un nom de prestataire.
///
/// ASYNCHRONE DEPUIS QUE LA CONFIGURATION VIT EN BASE. Les clés d'un PSP se règlent
/// désormais depuis la console d'administration : le choix de la passerelle et ses
/// réglages se lisent donc à l'APPEL, et non plus au démarrage du conteneur. Rendre
/// cette lecture synchrone obligerait à bloquer sur une tâche au milieu du chemin de
/// paiement — le dernier endroit où affamer le pool de threads.
/// </summary>
public interface IPaymentGatewayResolver
{
    Task<Result<IPaymentGateway>> ResolveAsync(string provider, CancellationToken cancellationToken = default);
}

/// <summary>
/// Résolveur par liste : prend les passerelles qu'on lui donne et les retrouve par
/// nom. Il ne lit aucune configuration.
///
/// C'est le résolveur des TESTS UNITAIRES, qui construisent leurs passerelles à la
/// main. En production, c'est RegistryPaymentGatewayResolver qui est enregistré :
/// lui seul lit le registre des fournisseurs et refuse une passerelle simulée.
/// </summary>
public sealed class PaymentGatewayResolver : IPaymentGatewayResolver
{
    private readonly IReadOnlyDictionary<string, IPaymentGateway> _gateways;

    public PaymentGatewayResolver(IEnumerable<IPaymentGateway> gateways)
        => _gateways = gateways.ToDictionary(g => g.Provider, StringComparer.OrdinalIgnoreCase);

    public Task<Result<IPaymentGateway>> ResolveAsync(string provider, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(provider) || !_gateways.TryGetValue(provider.Trim(), out var gateway))
        {
            return Task.FromResult(Result.Failure<IPaymentGateway>(Error.Validation(
                "payments.provider_unsupported", $"Prestataire de paiement non pris en charge : « {provider} ».")));
        }

        // Conversion implicite T -> Result<T> indisponible pour un type interface :
        // on construit le succès explicitement.
        return Task.FromResult(Result.Success(gateway));
    }
}
