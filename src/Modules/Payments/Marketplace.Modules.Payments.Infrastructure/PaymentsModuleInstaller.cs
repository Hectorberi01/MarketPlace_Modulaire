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
        var kkiapayOptions = BindKkiapayOptions(configuration);
        // Ces singletons ne servent plus aux ENCAISSEMENTS (le résolveur passe les
        // options qu'il vient de lire). Ils restent pour FedaPayPayoutGateway, qui
        // injecte FedaPayOptions et dont le choix est encore fait au démarrage —
        // voir plus bas.
        services.AddSingleton(stripeOptions);
        services.AddSingleton(paypalOptions);
        services.AddSingleton(mtnOptions);
        services.AddSingleton(moovOptions);
        services.AddSingleton(fedapayOptions);

        // ─────────────────────────────────────────────────────────────────────────
        // LE CHOIX DE LA PASSERELLE A QUITTÉ LE CONTENEUR, PAS LE GARDE-FOU.
        //
        // Les passerelles étaient choisies ICI, au démarrage : en production, une
        // passerelle SIMULÉE n'était jamais enregistrée. Ce mécanisme ne peut plus
        // tenir dès que les clés se règlent depuis la console — le conteneur ne
        // décide pas de ce qui changera après son démarrage.
        //
        // La garantie vit désormais dans RegistryPaymentGatewayResolver, qui refuse
        // de construire une passerelle simulée en production. Ce qui reste ici :
        // les clients HTTP nommés, enregistrés INCONDITIONNELLEMENT et SANS adresse
        // de base — elle vient maintenant des réglages lus à l'appel.
        // ─────────────────────────────────────────────────────────────────────────
        var isProduction = IsProduction(configuration);

        services.AddHttpClient(StripeHttpGateway.ClientName);
        services.AddHttpClient(PayPalHttpGateway.ClientName);
        services.AddHttpClient(MtnMomoHttpGateway.ClientName);
        services.AddHttpClient(MoovHttpGateway.ClientName);
        services.AddHttpClient(FedaPayHttpGateway.ClientName);

        // Ce que la CONFIGURATION contient, pour les gardes de démarrage ci-dessous.
        // Le registre peut en contenir davantage ; ces listes ne décrivent donc que
        // le repli, et c'est suffisant : elles servent à refuser un démarrage
        // manifestement cassé, pas à décrire l'état courant.
        var realGateways = new List<string>();
        var simulatedGateways = new List<string>();
        foreach (var (nom, configure) in new (string, bool)[]
                 {
                     ("Stripe", stripeOptions.IsConfigured),
                     ("PayPal", paypalOptions.IsConfigured),
                     ("MtnMomo", mtnOptions.IsConfigured),
                     ("Moov", moovOptions.IsConfigured),
                     ("FedaPay", fedapayOptions.IsConfigured),
                     // KkiaPay « configuré » ne suffit pas : en bac à sable le widget
                     // accepte des paiements fictifs et envoie quand même un rappel de
                     // succès. Il ne compte donc comme réel que hors sandbox.
                     ("Kkiapay", kkiapayOptions.CanCollectRealMoney)
                 })
        {
            (configure ? realGateways : simulatedGateways).Add(nom);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // UNE PASSERELLE RÉELLE SANS SECRET DE WEBHOOK N'EST PAS CONFIGURÉE.
        //
        // `IsConfigured` ne teste que la clé d'API — c'est-à-dire la capacité à
        // DEMANDER un encaissement. Rien ne testait la capacité à VÉRIFIER qu'il a eu
        // lieu. Une clé renseignée avec un secret de webhook vide enregistrait donc la
        // passerelle réelle, et celle-ci acceptait n'importe quel rappel non signé.
        //
        // Les deux garanties sont distinctes et toutes deux nécessaires. On les
        // sépare plutôt que d'élargir `IsConfigured`, dont le sens — « peut encaisser »
        // — reste juste et sert ailleurs.
        //
        // La seule protection existante vivait dans `docker-compose.prod.yml`, sous la
        // forme d'un `${VAR:?}`. Un fichier de déploiement n'est pas un garde-fou de
        // sécurité : il ne couvre pas le staging, il ne couvre pas un démarrage hors
        // compose, et il affirmait par ailleurs l'inverse du comportement réel.
        // ─────────────────────────────────────────────────────────────────────────
        if (isProduction)
        {
            var unverifiable = new List<string>();

            if (stripeOptions.IsConfigured && string.IsNullOrWhiteSpace(stripeOptions.WebhookSecret)) unverifiable.Add("Payments:Stripe:WebhookSecret");
            if (paypalOptions.IsConfigured && string.IsNullOrWhiteSpace(paypalOptions.WebhookSecret)) unverifiable.Add("Payments:PayPal:WebhookSecret");
            if (mtnOptions.IsConfigured && string.IsNullOrWhiteSpace(mtnOptions.WebhookSecret)) unverifiable.Add("Payments:MtnMomo:WebhookSecret");
            if (moovOptions.IsConfigured && string.IsNullOrWhiteSpace(moovOptions.WebhookSecret)) unverifiable.Add("Payments:Moov:WebhookSecret");
            if (fedapayOptions.IsConfigured && string.IsNullOrWhiteSpace(fedapayOptions.WebhookSecret)) unverifiable.Add("Payments:FedaPay:WebhookSecret");
            if (kkiapayOptions.IsConfigured && string.IsNullOrWhiteSpace(kkiapayOptions.WebhookSecret)) unverifiable.Add("Payments:Kkiapay:WebhookSecret");

            if (unverifiable.Count > 0)
            {
                throw new InvalidOperationException(
                    "PRESTATAIRE RÉEL SANS SECRET DE WEBHOOK.\n\n" +
                    $"Manquant(s) : {string.Join(", ", unverifiable)}.\n\n" +
                    "Sans ce secret, la route de rappel — anonyme et sans limitation de débit — ne peut " +
                    "vérifier aucune signature. N'importe qui pourrait déclarer une commande payée : stock " +
                    "décrémenté, expéditions créées, gains vendeur crédités, sans qu'un franc ait bougé. " +
                    "Les versements sont exposés au même risque.\n\n" +
                    "Le démarrage est refusé DÉLIBÉRÉMENT. Vérifiez l'injection des secrets.");
            }
        }

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
                $"[Payments] PASSERELLES SIMULÉES ACTIVES : {string.Join(", ", simulatedGateways)}. " +
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

        // SCOPED, et non singleton : il lit le registre des fournisseurs, lui-même
        // scoped (il tient un DbContext). Un singleton capturerait ce scope.
        services.AddScoped<IPaymentGatewayResolver, RegistryPaymentGatewayResolver>();

        services.AddScoped<IDomainEventHandler<PaymentCapturedDomainEvent>, PaymentCapturedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<PaymentFailedDomainEvent>, PaymentFailedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<PaymentRefundedDomainEvent>, PaymentRefundedDomainEventHandler>();

        // Chorégraphie : libération de l'escrow à la livraison de la commande.
        services.AddScoped<IIntegrationEventHandler<OrderDeliveredIntegrationEvent>, ReleaseEscrowOnOrderDeliveredHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<PaymentsDbContext>();
    }

    internal static StripeOptions BindStripeOptions(IConfiguration configuration)
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

    internal static PayPalOptions BindPayPalOptions(IConfiguration configuration)
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

    internal static MtnMomoOptions BindMtnMomoOptions(IConfiguration configuration)
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

    internal static FedaPayOptions BindFedaPayOptions(IConfiguration configuration)
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

    /// <summary>
    /// Sommes-nous en production ?
    ///
    /// L'installer ne reçoit qu'un <see cref="IConfiguration"/> — pas d'IHostEnvironment,
    /// car les modules s'installent avant que l'hôte ne soit construit. On lit donc la
    /// variable d'environnement standard, qu'ASP.NET expose dans la configuration.
    ///
    /// ATTENTION : FAIL-SAFE : les deux variables sont lues, et l'inconnu est traité comme
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

    internal static MoovOptions BindMoovOptions(IConfiguration configuration)
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

    internal static KkiapayOptions BindKkiapayOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Payments:Kkiapay");
        var options = new KkiapayOptions
        {
            PublicKey = section["PublicKey"] ?? string.Empty,
            WebhookSecret = section["WebhookSecret"] ?? string.Empty,
            CallbackUrl = section["CallbackUrl"] ?? string.Empty,
            // Bac à sable ACTIF sauf « false » explicite : une clé lue de travers ne
            // doit pas faire basculer en encaissement réel.
            Sandbox = !string.Equals(section["Sandbox"], "false", StringComparison.OrdinalIgnoreCase)
        };
        if (!string.IsNullOrWhiteSpace(section["Currency"]))
        {
            options.Currency = section["Currency"]!;
        }
        return options;
    }
}
