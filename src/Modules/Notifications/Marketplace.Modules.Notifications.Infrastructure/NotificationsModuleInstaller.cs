using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Notifications.Application.Abstractions;
using Marketplace.Modules.Notifications.Application.Notifications;
using Marketplace.Modules.Notifications.Application.Notifications.EventHandlers;
using Marketplace.Modules.Notifications.Application.Notifications.Queries;
using Marketplace.Modules.Notifications.Domain.Notifications;
using Marketplace.Modules.Notifications.Infrastructure.Persistence;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Shipping.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Notifications.Infrastructure;

/// <summary>Enregistre le module Notifications : DbContext, repository, dispatcher et consumers fan-out.</summary>
public sealed class NotificationsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Notifications";

    public Assembly ApplicationAssembly => typeof(ListMyNotificationsQuery).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<NotificationsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", NotificationsDbContext.SchemaName)));

        services.AddScoped<INotificationsUnitOfWork>(sp => sp.GetRequiredService<NotificationsDbContext>());

        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<NotificationDispatcher>();

        // Consumers fan-out : un fait métier d'un autre module -> une notification.
        services.AddScoped<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, OrderPlacedNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<OrderConfirmedIntegrationEvent>, OrderConfirmedNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<OrderCancelledIntegrationEvent>, OrderCancelledNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<ShipmentShippedIntegrationEvent>, ShipmentShippedNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<ShipmentDeliveredIntegrationEvent>, ShipmentDeliveredNotificationHandler>();

        services.AddHostedService<OutboxProcessor<NotificationsDbContext>>();
    }
}
