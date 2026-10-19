using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways.Real;
using Marketplace.Modules.Payments.Infrastructure.Gateways.Simulation;
using Marketplace.Modules.Platform.Contracts;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways;

/// <summary>
/// Choisit et construit la passerelle d'un PSP à partir du registre des
/// fournisseurs, avec repli sur la configuration.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LE GARDE-FOU DU CONTENEUR DÉPLACÉ, PAS SUPPRIMÉ.
///
/// Avant, les passerelles étaient choisies au DÉMARRAGE : en production, une
/// passerelle simulée n'était jamais enregistrée, et un PSP non configuré était
/// simplement absent du conteneur. Ce mécanisme-là disparaît forcément dès que les
/// clés se règlent depuis la console — le conteneur ne peut plus décider de ce qui
/// changera après son démarrage.
///
/// La garantie, elle, reste intacte et vit ICI : en production, une passerelle
/// SIMULÉE n'est JAMAIS construite. Un PSP non configuré produit une erreur franche
/// plutôt qu'un encaissement imaginaire. C'est ce que protégeait le code d'origine :
/// SimulatedPaymentGateway renvoie TOUJOURS « Captured », rembourse TOUJOURS avec
/// succès, et court-circuite la vérification de signature des webhooks.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class RegistryPaymentGatewayResolver : IPaymentGatewayResolver
{
    private readonly IPlatformProviderRegistry _registry;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _services;
    private readonly bool _isProduction;

    public RegistryPaymentGatewayResolver(
        IPlatformProviderRegistry registry, IConfiguration configuration, IServiceProvider services)
    {
        _registry = registry;
        _configuration = configuration;
        _services = services;

        var environment = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? string.Empty;
        _isProduction = string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<Result<IPaymentGateway>> ResolveAsync(
        string provider, CancellationToken cancellationToken = default)
    {
        var code = Normaliser(provider);
        if (code is null)
        {
            return Result.Failure<IPaymentGateway>(Error.Validation(
                "payments.provider_unsupported", $"Prestataire de paiement non pris en charge : « {provider} »."));
        }

        // `FindAsync` ne rend QUE les fournisseurs activés : un PSP enregistré mais
        // désactivé depuis la console se comporte exactement comme un PSP absent.
        var registre = await _registry.FindAsync(PlatformChannel.Payment, code, cancellationToken);

        return code switch
        {
            ProviderCodes.Stripe => Construire(
                PaymentProviderOptionsFactory.Stripe(registre, _configuration),
                o => o.IsConfigured,
                o => ActivatorUtilities.CreateInstance<StripeHttpGateway>(_services, o),
                o => new StripePaymentGateway(o),
                code),

            ProviderCodes.PayPal => Construire(
                PaymentProviderOptionsFactory.PayPal(registre, _configuration),
                o => o.IsConfigured,
                o => ActivatorUtilities.CreateInstance<PayPalHttpGateway>(_services, o),
                o => new PayPalPaymentGateway(o),
                code),

            ProviderCodes.MtnMomo => Construire(
                PaymentProviderOptionsFactory.MtnMomo(registre, _configuration),
                o => o.IsConfigured,
                o => ActivatorUtilities.CreateInstance<MtnMomoHttpGateway>(_services, o),
                o => new MtnMomoPaymentGateway(o),
                code),

            ProviderCodes.Moov => Construire(
                PaymentProviderOptionsFactory.Moov(registre, _configuration),
                o => o.IsConfigured,
                o => ActivatorUtilities.CreateInstance<MoovHttpGateway>(_services, o),
                o => new MoovPaymentGateway(o),
                code),

            ProviderCodes.FedaPay => ConstruireFedaPay(
                PaymentProviderOptionsFactory.FedaPay(registre, _configuration)),

            ProviderCodes.Kkiapay => ConstruireKkiapay(
                PaymentProviderOptionsFactory.Kkiapay(registre, _configuration)),

            _ => Result.Failure<IPaymentGateway>(Error.Validation(
                "payments.provider_unsupported", $"Prestataire de paiement non pris en charge : « {provider} »."))
        };
    }

    private Result<IPaymentGateway> Construire<TOptions>(
        TOptions options,
        Func<TOptions, bool> estConfigure,
        Func<TOptions, IPaymentGateway> reelle,
        Func<TOptions, IPaymentGateway> simulee,
        string code)
    {
        if (estConfigure(options))
        {
            return Result.Success(reelle(options));
        }

        if (_isProduction)
        {
            return Result.Failure<IPaymentGateway>(Error.Validation(
                "payments.provider_not_configured",
                $"{code} n'est pas configuré : aucun encaissement n'est tenté. "
                + "Renseignez ses clés dans la console d'administration, puis activez-le."));
        }

        return Result.Success(simulee(options));
    }

    private Result<IPaymentGateway> ConstruireFedaPay(FedaPayOptions options)
    {
        // Une clé et une URL qui ne désignent pas le même monde, c'est le pire des
        // cas : soit tout échoue en 403 sans qu'on comprenne pourquoi, soit — si la
        // clé est live — de l'argent RÉEL part alors qu'on croyait tester. C'était une
        // garde de démarrage ; elle est ici parce que la BaseUrl se règle désormais
        // depuis la console.
        if (options.IsConfigured && !options.KeyMatchesEnvironment)
        {
            return Result.Failure<IPaymentGateway>(Error.Validation(
                "payments.fedapay_environment_mismatch",
                "Configuration FedaPay incohérente : la clé API et l'URL de base ne désignent pas le même "
                + $"environnement (BaseUrl = « {options.BaseUrl} »). Une clé sk_live_… exige "
                + "https://api.fedapay.com/v1 ; une clé sk_sandbox_… exige https://sandbox-api.fedapay.com/v1."));
        }

        return Construire(
            options,
            o => o.IsConfigured,
            o => ActivatorUtilities.CreateInstance<FedaPayHttpGateway>(_services, o),
            o => new FedaPayPaymentGateway(o),
            ProviderCodes.FedaPay);
    }

    /// <summary>
    /// KkiaPay en BAC À SABLE est une passerelle simulée, quel que soit le reste de
    /// sa configuration : le widget accepte des paiements fictifs et envoie quand
    /// même un rappel de succès signé. La commande serait donc marquée payée, stock
    /// décrémenté et gains vendeur crédités, sans qu'un franc ait bougé.
    ///
    /// C'est l'équivalent, pour KkiaPay, du contrôle clé/URL de FedaPay : un réglage
    /// qui n'a l'air de rien et qui décide si l'argent est réel.
    /// </summary>
    private Result<IPaymentGateway> ConstruireKkiapay(KkiapayOptions options)
    {
        if (_isProduction && options.IsConfigured && options.Sandbox)
        {
            return Result.Failure<IPaymentGateway>(Error.Validation(
                "payments.kkiapay_sandbox_in_production",
                "KkiaPay est configuré en BAC À SABLE : aucun encaissement réel n'est possible. "
                + "Désactivez le mode bac à sable dans la console d'administration, avec une clé publique live."));
        }

        return Construire(
            options,
            o => o.CanCollectRealMoney,
            o => new KkiapayWidgetGateway(o),
            o => new KkiapaySimulatedGateway(o),
            ProviderCodes.Kkiapay);
    }

    /// <summary>
    /// Ramène le nom reçu (« stripe », « MOOV »…) sur le code exact du catalogue.
    /// La comparaison est insensible à la casse, comme l'était le résolveur par
    /// dictionnaire qu'il remplace.
    /// </summary>
    private static string? Normaliser(string provider)
    {
        var nom = (provider ?? string.Empty).Trim();
        return ProviderCodes.All.FirstOrDefault(c => string.Equals(c, nom, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Codes des PSP, tels qu'ils apparaissent dans le registre ET dans la propriété
/// <c>IPaymentGateway.Provider</c>. Les deux doivent coïncider : c'est ce nom que
/// l'acheteur envoie à l'initiation du paiement.
/// </summary>
internal static class ProviderCodes
{
    public const string Stripe = "Stripe";
    public const string PayPal = "PayPal";
    public const string MtnMomo = "MtnMomo";
    public const string Moov = "Moov";
    public const string FedaPay = "FedaPay";
    public const string Kkiapay = "Kkiapay";

    public static readonly string[] All = { Stripe, PayPal, MtnMomo, Moov, FedaPay, Kkiapay };
}
