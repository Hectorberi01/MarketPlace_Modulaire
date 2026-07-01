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

        // Chaque PSP : adaptateur HTTP réel si configuré, sinon stub sandbox
        // (build/run fonctionnels sans compte PSP).
        if (stripeOptions.IsConfigured)
        {
            services.AddHttpClient(StripeHttpGateway.ClientName, client =>
                client.BaseAddress = new Uri(EnsureTrailingSlash(stripeOptions.BaseUrl)));
            services.AddSingleton<IPaymentGateway, StripeHttpGateway>();
        }
        else
        {
            services.AddSingleton<IPaymentGateway, StripePaymentGateway>();
        }

        if (paypalOptions.IsConfigured)
        {
            services.AddHttpClient(PayPalHttpGateway.ClientName, client =>
                client.BaseAddress = new Uri(EnsureTrailingSlash(paypalOptions.BaseUrl)));
            services.AddSingleton<IPaymentGateway, PayPalHttpGateway>();
        }
        else
        {
            services.AddSingleton<IPaymentGateway, PayPalPaymentGateway>();
        }

        if (mtnOptions.IsConfigured)
        {
            services.AddHttpClient(MtnMomoHttpGateway.ClientName, client =>
                client.BaseAddress = new Uri(EnsureTrailingSlash(mtnOptions.BaseUrl)));
            services.AddSingleton<IPaymentGateway, MtnMomoHttpGateway>();
        }
        else
        {
            services.AddSingleton<IPaymentGateway, MtnMomoPaymentGateway>();
        }

        if (moovOptions.IsConfigured)
        {
            services.AddHttpClient(MoovHttpGateway.ClientName, client =>
                client.BaseAddress = new Uri(EnsureTrailingSlash(moovOptions.BaseUrl)));
            services.AddSingleton<IPaymentGateway, MoovHttpGateway>();
        }
        else
        {
            services.AddSingleton<IPaymentGateway, MoovPaymentGateway>();
        }

        if (fedapayOptions.IsConfigured)
        {
            services.AddHttpClient(FedaPayHttpGateway.ClientName, client =>
                client.BaseAddress = new Uri(EnsureTrailingSlash(fedapayOptions.BaseUrl)));
            services.AddSingleton<IPaymentGateway, FedaPayHttpGateway>();
            services.AddSingleton<IPayoutGateway, FedaPayPayoutGateway>();
        }
        else
        {
            services.AddSingleton<IPaymentGateway, FedaPayPaymentGateway>();
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
            CallbackUrl = section["CallbackUrl"] ?? string.Empty
        };
        if (!string.IsNullOrWhiteSpace(section["BaseUrl"]))
        {
            options.BaseUrl = section["BaseUrl"]!;
        }
        if (!string.IsNullOrWhiteSpace(section["Currency"]))
        {
            options.Currency = section["Currency"]!;
        }
        return options;
    }

    // BaseAddress doit finir par « / » pour que les URI relatifs des requêtes se concatènent correctement.
    private static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : url + "/";

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
