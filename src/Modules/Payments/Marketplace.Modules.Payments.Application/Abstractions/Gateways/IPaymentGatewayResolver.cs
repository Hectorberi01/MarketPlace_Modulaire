using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Payments.Application.Abstractions.Gateways;

/// <summary>Sélectionne l'implémentation PSP correspondant à un nom de prestataire.</summary>
public interface IPaymentGatewayResolver
{
    Result<IPaymentGateway> Resolve(string provider);
}

/// <summary>
/// Résout le bon <see cref="IPaymentGateway"/> parmi ceux enregistrés, par nom
/// (insensible à la casse). N'a aucune dépendance d'infrastructure : il consomme
/// la collection de ports injectée par le conteneur.
/// </summary>
public sealed class PaymentGatewayResolver : IPaymentGatewayResolver
{
    private readonly IReadOnlyDictionary<string, IPaymentGateway> _gateways;

    public PaymentGatewayResolver(IEnumerable<IPaymentGateway> gateways)
        => _gateways = gateways.ToDictionary(g => g.Provider, StringComparer.OrdinalIgnoreCase);

    public Result<IPaymentGateway> Resolve(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider) || !_gateways.TryGetValue(provider.Trim(), out var gateway))
        {
            return Error.Validation("payments.provider_unsupported", $"Prestataire de paiement non pris en charge : « {provider} ».");
        }

        // Conversion implicite T -> Result<T> indisponible pour un type interface :
        // on construit le succès explicitement.
        return Result.Success(gateway);
    }
}
