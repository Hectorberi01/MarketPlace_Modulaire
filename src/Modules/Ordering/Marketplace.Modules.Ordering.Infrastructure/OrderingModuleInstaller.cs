using Microsoft.Extensions.DependencyInjection.Extensions;
using Marketplace.BuildingBlocks.Application.Options;
using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Hosting;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Ordering.Application.Abstractions;
using Marketplace.Modules.Ordering.Application.Orders.Commands.PlaceOrder;
using Marketplace.Modules.Ordering.Application.Orders.EventHandlers;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Ordering.Domain.Orders;
using Marketplace.Modules.Ordering.Domain.Orders.Events;
using Marketplace.Modules.Ordering.Infrastructure.Persistence;
using Marketplace.Modules.Ordering.Infrastructure.Public;
using Marketplace.Modules.Payments.Contracts.IntegrationEvents;
using Marketplace.Modules.Shipping.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Ordering.Infrastructure;

/// <summary>Enregistre le module Ordering : DbContext, repository, API publique, Saga handlers, validators, outbox.</summary>
public sealed class OrderingModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Ordering";

    public Assembly ApplicationAssembly => typeof(PlaceOrderCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // ─────────────────────────────────────────────────────────────────────────
        // LE TAUX DU FRAIS DE SERVICE, LU PAR LES DEUX MODULES QUI EN ONT BESOIN.
        //
        // Cart l'ANNONCE dans le panier, Ordering le FIGE sur la commande. Les deux
        // installeurs enregistrent donc le même singleton depuis la même section :
        // `TryAddSingleton` fait que le premier chargé gagne et que le second ne le
        // remplace pas — deux instances porteraient de toute façon la même valeur,
        // mais l'écrasement silencieux est le genre de détail dont on ne veut pas
        // dépendre de l'ordre de chargement des modules.
        // ─────────────────────────────────────────────────────────────────────────
        services.TryAddSingleton(PlatformFeeOptions.Depuis(configuration.GetSection("Pricing")["PlatformFeeRate"]));

        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<OrderingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", OrderingDbContext.SchemaName)));

        services.AddScoped<IOrderingUnitOfWork>(sp => sp.GetRequiredService<OrderingDbContext>());

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IOrderingModuleApi, OrderingModuleApi>();

        services.AddScoped<IDomainEventHandler<OrderPlacedDomainEvent>, OrderPlacedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<OrderConfirmedDomainEvent>, OrderConfirmedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<OrderCancelledDomainEvent>, OrderCancelledDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<OrderDeliveredDomainEvent>, OrderDeliveredDomainEventHandler>();

        // Suite du Saga : réactions aux résultats de paiement.
        services.AddScoped<IIntegrationEventHandler<PaymentCapturedIntegrationEvent>, ConfirmOrderOnPaymentCapturedHandler>();
        services.AddScoped<IIntegrationEventHandler<PaymentFailedIntegrationEvent>, CancelOrderOnPaymentFailedHandler>();

        // Étape finale du Saga : marquer la commande livrée quand toutes ses
        // expéditions le sont (déclenche escrow + payout vendeur).
        services.AddScoped<IIntegrationEventHandler<ShipmentDeliveredIntegrationEvent>, MarkOrderDeliveredOnAllShipmentsDeliveredHandler>();

        // Expiration des commandes jamais payées. Sans elle, chacune retient du stock
        // invendable et une cagnotte client immobilisée, indéfiniment.
        //
        // UN SEUL HÔTE. Les cinq hôtes composent les mêmes modules : sans cette
        // condition, cinq balayeurs annuleraient les mêmes commandes en même temps.
        if (BackgroundJobs.EnabledOnThisHost)
        {
            services.AddHostedService<Maintenance.AbandonedOrderSweeper>();
        }

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<OrderingDbContext>();
    }
}
