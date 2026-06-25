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
using Marketplace.Modules.Ordering.Application.Abstractions;
using Marketplace.Modules.Ordering.Application.Orders.Commands.PlaceOrder;
using Marketplace.Modules.Ordering.Application.Orders.EventHandlers;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Ordering.Domain.Orders;
using Marketplace.Modules.Ordering.Domain.Orders.Events;
using Marketplace.Modules.Ordering.Infrastructure.Persistence;
using Marketplace.Modules.Ordering.Infrastructure.Public;
using Marketplace.Modules.Payments.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Ordering.Infrastructure;

/// <summary>Enregistre le module Ordering : DbContext, repository, API publique, Saga handlers, validators, outbox.</summary>
public sealed class OrderingModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Ordering";

    public Assembly ApplicationAssembly => typeof(PlaceOrderCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
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

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddHostedService<OutboxProcessor<OrderingDbContext>>();
    }
}
