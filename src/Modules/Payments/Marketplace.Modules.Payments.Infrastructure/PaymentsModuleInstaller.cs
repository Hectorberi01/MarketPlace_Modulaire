using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Payments.Application.Abstractions;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;
using Marketplace.Modules.Payments.Application.Payments.Commands.InitiatePayment;
using Marketplace.Modules.Payments.Application.Payments.EventHandlers;
using Marketplace.Modules.Payments.Contracts;
using Marketplace.Modules.Payments.Domain.Payments;
using Marketplace.Modules.Payments.Domain.Payments.Events;
using Marketplace.Modules.Payments.Infrastructure.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways.Real;
using Marketplace.Modules.Payments.Infrastructure.Gateways.Simulation;
using Marketplace.Modules.Payments.Infrastructure.Persistence;
using Marketplace.Modules.Payments.Infrastructure.Public;

namespace Marketplace.Modules.Payments.Infrastructure;

/// <summary>Enregistre le module Payments : DbContext, repository, API publique, handlers, validators, outbox.</summary>
public sealed class PaymentsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Payments";

    public Assembly ApplicationAssembly => typeof(InitiatePaymentCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<PaymentsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", PaymentsDbContext.SchemaName)));

        services.AddScoped<IPaymentsUnitOfWork>(sp => sp.GetRequiredService<PaymentsDbContext>());

        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentsModuleApi, PaymentsModuleApi>();

        // Prestataires de paiement : adaptateurs + résolveur par nom.
        // Options liées manuellement depuis la config (aucune dépendance IOptions).
        var stripeOptions = BindStripeOptions(configuration);
        var paypalOptions = BindPayPalOptions(configuration);
        var mtnOptions = BindMtnMomoOptions(configuration);
        var moovOptions = BindMoovOptions(configuration);
        var fedapayOptions = BindFedaPayOptions(configuration);
        services.AddSingleton(stripeOptions);
        services.AddSingleton(paypalOptions);
        services.AddSingleton(mtnOptions);
        services.AddSingleton(moovOptions);
        services.AddSingleton(fedapayOptions);

        // ─────────────────────────────────────────────────────────────────────────
        // ⚠️ LE GARDE-FOU LE PLUS IMPORTANT DE TOUT LE PROJET.
        //
        // AVANT : chaque PSP non configuré retombait SILENCIEUSEMENT sur une passerelle
        // SIMULÉE. Or les clés sont vides par défaut dans appsettings.json. Un
        // déploiement qui oubliait d'injecter les secrets démarrait donc normalement…
        // et simulait TOUS les paiements :
        //
        //   • SimulatedPaymentGateway.GetStatusAsync() renvoie TOUJOURS « Captured » ;
        //   • RefundAsync() renvoie TOUJOURS « Success » ;
        //   • la vérification de signature des webhooks est court-circuitée.
        //
        // Autrement dit : la plateforme encaissait des commandes marquées PAYÉES sans
        // qu'un seul franc ne bouge, et acceptait n'importe quel faux webhook. Aucune
        // erreur, aucun log, aucun symptôme — jusqu'à ce qu'un vendeur réclame l'argent
        // d'une vente qui n'a jamais été payée.
        //
        // DÉSORMAIS : en PRODUCTION, une passerelle simulée n'est JAMAIS enregistrée.
        // Un PSP non configuré est simplement ABSENT du conteneur, et le résolveur
        // renvoie une erreur franche (« Prestataire de paiement non pris en charge »).
        // Un paiement impossible vaut infiniment mieux qu'un paiement imaginaire.
        //
        // Et si AUCUNE passerelle réelle n'est configurée en production, on refuse de
        // démarrer (voir plus bas). Une plateforme qui ne boote pas se répare en cinq
        // minutes ; une plateforme qui simule ses encaissements, jamais tout à fait.
        // ─────────────────────────────────────────────────────────────────────────
        var isProduction = IsProduction(configuration);
        var realGateways = new List<string>();
        var simulatedGateways = new List<string>();

        RegisterGateway(
            services, isProduction, stripeOptions.IsConfigured, "Stripe", realGateways, simulatedGateways,
            registerReal: () =>
            {
                services.AddHttpClient(StripeHttpGateway.ClientName, client =>
                    client.BaseAddress = new Uri(EnsureTrailingSlash(stripeOptions.BaseUrl)));
                services.AddSingleton<IPaymentGateway, StripeHttpGateway>();
            },
            registerSimulated: () => services.AddSingleton<IPaymentGateway, StripePaymentGateway>());

        RegisterGateway(
            services, isProduction, paypalOptions.IsConfigured, "PayPal", realGateways, simulatedGateways,
            registerReal: () =>
            {
                services.AddHttpClient(PayPalHttpGateway.ClientName, client =>
                    client.BaseAddress = new Uri(EnsureTrailingSlash(paypalOptions.BaseUrl)));
                services.AddSingleton<IPaymentGateway, PayPalHttpGateway>();
            },
            registerSimulated: () => services.AddSingleton<IPaymentGateway, PayPalPaymentGateway>());

        RegisterGateway(
            services, isProduction, mtnOptions.IsConfigured, "MtnMomo", realGateways, simulatedGateways,
            registerReal: () =>
            {
                services.AddHttpClient(MtnMomoHttpGateway.ClientName, client =>
                    client.BaseAddress = new Uri(EnsureTrailingSlash(mtnOptions.BaseUrl)));
                services.AddSingleton<IPaymentGateway, MtnMomoHttpGateway>();
            },
            registerSimulated: () => services.AddSingleton<IPaymentGateway, MtnMomoPaymentGateway>());

        RegisterGateway(
            services, isProduction, moovOptions.IsConfigured, "Moov", realGateways, simulatedGateways,
            registerReal: () =>
            {
                services.AddHttpClient(MoovHttpGateway.ClientName, client =>
                    client.BaseAddress = new Uri(EnsureTrailingSlash(moovOptions.BaseUrl)));
                services.AddSingleton<IPaymentGateway, MoovHttpGateway>();
            },
            registerSimulated: () => services.AddSingleton<IPaymentGateway, MoovPaymentGateway>());

        RegisterGateway(
            services, isProduction, fedapayOptions.IsConfigured, "FedaPay", realGateways, simulatedGateways,
            registerReal: () =>
            {
                services.AddHttpClient(FedaPayHttpGateway.ClientName, client =>
                    client.BaseAddress = new Uri(EnsureTrailingSlash(fedapayOptions.BaseUrl)));
                services.AddSingleton<IPaymentGateway, FedaPayHttpGateway>();
            },
            registerSimulated: () => services.AddSingleton<IPaymentGateway, FedaPayPaymentGateway>());

        // En production, une plateforme SANS aucun moyen d'encaisser n'est pas une
        // plateforme : c'est un catalogue. On refuse de démarrer plutôt que d'accepter
        // des commandes qu'on ne saura jamais faire payer.
        if (isProduction && realGateways.Count == 0)
        {
            throw new InvalidOperationException(
                "PRODUCTION SANS AUCUN PRESTATAIRE DE PAIEMENT CONFIGURÉ.\n\n" +
                "Aucune clé PSP n'a été trouvée (Payments:FedaPay:ApiKey, Payments:Stripe:ApiKey, …).\n" +
                "Le démarrage est refusé DÉLIBÉRÉMENT : sans ce garde-fou, la plateforme retomberait sur des " +
                "passerelles SIMULÉES qui marquent toute commande comme « payée » sans qu'aucun argent ne bouge.\n\n" +
                "Vérifiez l'injection des secrets (vault Ansible / variables d'environnement du conteneur).");
        }

        // Bruyant, et volontairement. En production ce cas est impossible (voir
        // ci-dessus) ; ailleurs, il faut qu'un développeur qui teste un paiement sache
        // qu'il teste une FICTION — sans quoi il conclura que « ça marche ».
        if (simulatedGateways.Count > 0)
        {
            Console.WriteLine(
                $"[Payments] ⚠️  PASSERELLES SIMULÉES ACTIVES : {string.Join(", ", simulatedGateways)}. " +
                "Ces prestataires acceptent TOUT paiement sans qu'aucun argent ne bouge. " +
                (realGateways.Count > 0
                    ? $"Passerelles réelles : {string.Join(", ", realGateways)}."
                    : "AUCUNE passerelle réelle configurée."));
        }

        // Une clé et une URL qui ne désignent pas le même monde, c'est le pire des
        // cas : soit tout échoue en 403 sans qu'on comprenne pourquoi, soit — si la
        // clé est live — de l'argent RÉEL part alors qu'on croyait tester. On refuse
        // de démarrer. Une plateforme de paiement qui ne boote pas se répare en cinq
        // minutes ; une plateforme qui paie au mauvais endroit, jamais tout à fait.
        if (fedapayOptions.IsConfigured && !fedapayOptions.KeyMatchesEnvironment)
        {
            throw new InvalidOperationException(
                "Configuration FedaPay incohérente : la clé API et Payments:FedaPay:BaseUrl ne désignent pas le même environnement " +
                $"(BaseUrl = « {fedapayOptions.BaseUrl} »). Une clé sk_live_… exige https://api.fedapay.com/v1 ; " +
                "une clé sk_sandbox_… exige https://sandbox-api.fedapay.com/v1.");
        }

        // Versements (payouts vendeur) : réels via FedaPay UNIQUEMENT en LIVE, clé
        // configurée et flag activé — voir FedaPayOptions.CanPayout.
        //
        // Le bac à sable FedaPay N'EXÉCUTE PAS les dépôts Mobile Money : il refuse la
        // création avec « 403 Opération non autorisée ». Y activer les payouts ne
        // produisait qu'une file de retraits en échec, remboursés, et un vendeur
        // persuadé que la plateforme lui devait de l'argent. On simule donc, et on le
        // DIT — plutôt que d'échouer en silence.
        if (fedapayOptions.CanPayout)
        {
            services.AddSingleton<IPayoutGateway, FedaPayPayoutGateway>();
        }
        else
        {
            if (fedapayOptions.EnablePayouts && fedapayOptions.IsSandbox)
            {
                // Cette combinaison est une erreur de configuration, pas un choix :
                // elle mérite une trace explicite au démarrage.
                Console.Error.WriteLine(
                    "[FedaPay] EnablePayouts=true avec l'API bac à sable : les versements réels sont IMPOSSIBLES " +
                    "en sandbox (403). Les retraits seront SIMULÉS. Pour des versements réels, passez " +
                    "Payments:FedaPay:BaseUrl sur https://api.fedapay.com/v1 avec une clé sk_live_…, et faites " +
                    "activer les dépôts sur le compte marchand par FedaPay.");
            }

            services.AddSingleton<IPayoutGateway, SimulatedPayoutGateway>();
        }

        services.AddScoped<IPayoutModuleApi, PayoutModuleApi>();

        services.AddSingleton<IPaymentGatewayResolver, PaymentGatewayResolver>();

        services.AddScoped<IDomainEventHandler<PaymentCapturedDomainEvent>, PaymentCapturedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<PaymentFailedDomainEvent>, PaymentFailedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<PaymentRefundedDomainEvent>, PaymentRefundedDomainEventHandler>();

        // Chorégraphie : libération de l'escrow à la livraison de la commande.
        services.AddScoped<IIntegrationEventHandler<OrderDeliveredIntegrationEvent>, ReleaseEscrowOnOrderDeliveredHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<PaymentsDbContext>();
    }

    private static StripeOptions BindStripeOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Payments:Stripe");
        var options = new StripeOptions
        {
            ApiKey = section["ApiKey"] ?? string.Empty,
            WebhookSecret = section["WebhookSecret"] ?? string.Empty,
            SuccessUrl = section["SuccessUrl"] ?? string.Empty,
            CancelUrl = section["CancelUrl"] ?? string.Empty
        };
        if (!string.IsNullOrWhiteSpace(section["BaseUrl"]))
        {
            options.BaseUrl = section["BaseUrl"]!;
        }
        if (!string.IsNullOrWhiteSpace(section["CheckoutBaseUrl"]))
        {
            options.CheckoutBaseUrl = section["CheckoutBaseUrl"]!;
        }
        return options;
    }

    private static PayPalOptions BindPayPalOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Payments:PayPal");
        var options = new PayPalOptions
        {
            ClientId = section["ClientId"] ?? string.Empty,
            Secret = section["Secret"] ?? string.Empty,
            WebhookId = section["WebhookId"] ?? string.Empty,
            WebhookSecret = section["WebhookSecret"] ?? string.Empty,
            ReturnUrl = section["ReturnUrl"] ?? string.Empty,
            CancelUrl = section["CancelUrl"] ?? string.Empty
        };
        if (!string.IsNullOrWhiteSpace(section["BaseUrl"]))
        {
            options.BaseUrl = section["BaseUrl"]!;
        }
        if (!string.IsNullOrWhiteSpace(section["CheckoutBaseUrl"]))
        {
            options.CheckoutBaseUrl = section["CheckoutBaseUrl"]!;
        }
        return options;
    }

    private static MtnMomoOptions BindMtnMomoOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Payments:MtnMomo");
        var options = new MtnMomoOptions
        {
            SubscriptionKey = section["SubscriptionKey"] ?? string.Empty,
            ApiUser = section["ApiUser"] ?? string.Empty,
            ApiKey = section["ApiKey"] ?? string.Empty,
            WebhookSecret = section["WebhookSecret"] ?? string.Empty,
            CallbackUrl = section["CallbackUrl"] ?? string.Empty
        };
        if (!string.IsNullOrWhiteSpace(section["Currency"]))
        {
            options.Currency = section["Currency"]!;
        }
        if (!string.IsNullOrWhiteSpace(section["BaseUrl"]))
        {
            options.BaseUrl = section["BaseUrl"]!;
        }
        if (!string.IsNullOrWhiteSpace(section["TargetEnvironment"]))
        {
            options.TargetEnvironment = section["TargetEnvironment"]!;
        }
        return options;
    }

    private static FedaPayOptions BindFedaPayOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Payments:FedaPay");
        var options = new FedaPayOptions
        {
            ApiKey = section["ApiKey"] ?? string.Empty,
            WebhookSecret = section["WebhookSecret"] ?? string.Empty,
            CallbackUrl = section["CallbackUrl"] ?? string.Empty,
            // Active les versements réels FedaPay (sinon payout simulé). Clé lue
            // depuis Payments__FedaPay__EnablePayouts (« true »/« false »).
            EnablePayouts = bool.TryParse(section["EnablePayouts"], out var enablePayouts) && enablePayouts
        };
        if (!string.IsNullOrWhiteSpace(section["BaseUrl"]))
        {
            options.BaseUrl = section["BaseUrl"]!;
        }
        if (!string.IsNullOrWhiteSpace(section["Currency"]))
        {
            options.Currency = section["Currency"]!;
        }
        // Méthode de transfert payout (« mode » FedaPay). Lue depuis
        // Payments__FedaPay__PayoutMode. Défaut mtn_open (MTN Mobile Money Bénin).
        if (!string.IsNullOrWhiteSpace(section["PayoutMode"]))
        {
            options.PayoutMode = section["PayoutMode"]!;
        }
        return options;
    }

    // BaseAddress doit finir par « / » pour que les URI relatifs des requêtes se concatènent correctement.
    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";

    /// <summary>
    /// Enregistre un PSP : l'adaptateur RÉEL s'il est configuré ; sinon la simulation —
    /// et UNIQUEMENT hors production.
    ///
    /// En production, un PSP non configuré n'est pas remplacé : il est ABSENT. Le
    /// résolveur renverra alors « Prestataire de paiement non pris en charge », ce qui
    /// est une erreur honnête. Le remplacer par une simulation produirait, à l'inverse,
    /// un encaissement fictif — un mensonge silencieux, et le pire bug possible pour
    /// une plateforme qui manipule de l'argent.
    /// </summary>
    private static void RegisterGateway(
        IServiceCollection services,
        bool isProduction,
        bool isConfigured,
        string providerName,
        List<string> realGateways,
        List<string> simulatedGateways,
        Action registerReal,
        Action registerSimulated)
    {
        if (isConfigured)
        {
            registerReal();
            realGateways.Add(providerName);
            return;
        }

        if (isProduction)
        {
            // Non configuré ET en production : on n'enregistre RIEN. Ce prestataire
            // n'existe tout simplement pas pour cette instance.
            return;
        }

        registerSimulated();
        simulatedGateways.Add(providerName);
    }

    /// <summary>
    /// Sommes-nous en production ?
    ///
    /// L'installer ne reçoit qu'un <see cref="IConfiguration"/> — pas d'IHostEnvironment,
    /// car les modules s'installent avant que l'hôte ne soit construit. On lit donc la
    /// variable d'environnement standard, qu'ASP.NET expose dans la configuration.
    ///
    /// ⚠️ FAIL-SAFE : les deux variables sont lues, et l'inconnu est traité comme
    /// « pas la production » — sinon un environnement mal nommé refuserait de démarrer
    /// en développement. Le risque assumé est l'inverse : une PROD dont
    /// ASPNETCORE_ENVIRONMENT serait mal renseigné retomberait sur des simulations.
    /// C'est pourquoi le message d'avertissement ci-dessus est aussi bruyant : il doit
    /// être visible dans les logs de démarrage, même quand personne ne les cherche.
    /// </summary>
    private static bool IsProduction(IConfiguration configuration)
    {
        var environment = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? string.Empty;

        return string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase);
    }

    private static MoovOptions BindMoovOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Payments:Moov");
        var options = new MoovOptions
        {
            MerchantId = section["MerchantId"] ?? string.Empty,
            ApiKey = section["ApiKey"] ?? string.Empty,
            WebhookSecret = section["WebhookSecret"] ?? string.Empty,
            CallbackUrl = section["CallbackUrl"] ?? string.Empty
        };
        if (!string.IsNullOrWhiteSpace(section["Currency"]))
        {
            options.Currency = section["Currency"]!;
        }
        if (!string.IsNullOrWhiteSpace(section["BaseUrl"]))
        {
            options.BaseUrl = section["BaseUrl"]!;
        }
        if (!string.IsNullOrWhiteSpace(section["TokenPath"]))
        {
            options.TokenPath = section["TokenPath"]!;
        }
        if (!string.IsNullOrWhiteSpace(section["PaymentPath"]))
        {
            options.PaymentPath = section["PaymentPath"]!;
        }
        return options;
    }
}
