using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.IntegrationEvents;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using FirebaseAdmin.Messaging;
using Marketplace.Modules.Notifications.Application.Abstractions;
using Marketplace.Modules.Notifications.Application.Notifications;
using Marketplace.Modules.Notifications.Application.Notifications.EventHandlers;
using Marketplace.Modules.Notifications.Application.Notifications.Queries;
using Marketplace.Modules.Notifications.Domain.Devices;
using Marketplace.Modules.Notifications.Domain.Notifications;
using Marketplace.Modules.Notifications.Infrastructure.Persistence;
using Marketplace.Modules.Notifications.Infrastructure.Push;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Shipping.Contracts.IntegrationEvents;
// Lève l'ambiguïté avec FirebaseAdmin.Messaging.FcmOptions.
using FcmOptions = Marketplace.Modules.Notifications.Infrastructure.Push.FcmOptions;

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
        services.AddScoped<IDeviceTokenRepository, DeviceTokenRepository>();
        services.AddScoped<NotificationDispatcher>();

        // Push (FCM) : si un compte de service Firebase est configuré, on branche
        // l'envoi réel ; sinon un adaptateur no-op (aucun push, mais tout compile/tourne).
        var fcm = BindFcmOptions(configuration);
        services.AddSingleton(fcm);
        if (fcm.IsConfigured)
        {
            if (FirebaseApp.DefaultInstance is null)
            {
                FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromJson(fcm.ResolveJson()) });
            }
            services.AddSingleton(FirebaseMessaging.DefaultInstance);
            services.AddScoped<IPushSender, FcmPushSender>();
            Console.WriteLine("[Push] FCM configuré : FcmPushSender actif (les push seront envoyés).");
        }
        else
        {
            services.AddScoped<IPushSender, NullPushSender>();
            // DIAGNOSTIC : aucun compte de service FCM résolu -> AUCUN push ne partira.
            // Vérifier le montage du fichier et Notifications:Fcm:ServiceAccountPath/Json.
            Console.WriteLine(
                $"[Push] FCM NON configuré : NullPushSender actif (aucun push). " +
                $"Path='{fcm.ServiceAccountPath}', JsonInline={( string.IsNullOrWhiteSpace(fcm.ServiceAccountJson) ? "non" : "oui")}, " +
                $"FichierExiste={(!string.IsNullOrWhiteSpace(fcm.ServiceAccountPath) && File.Exists(fcm.ServiceAccountPath) ? "oui" : "non")}.");
        }

        // Consumers fan-out : un fait métier d'un autre module -> une notification.
        services.AddScoped<IIntegrationEventHandler<OrderPlacedIntegrationEvent>, OrderPlacedNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<OrderConfirmedIntegrationEvent>, OrderConfirmedNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<OrderCancelledIntegrationEvent>, OrderCancelledNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<ShipmentShippedIntegrationEvent>, ShipmentShippedNotificationHandler>();
        services.AddScoped<IIntegrationEventHandler<ShipmentDeliveredIntegrationEvent>, ShipmentDeliveredNotificationHandler>();

        services.AddOutboxProcessor<NotificationsDbContext>();
    }

    private static FcmOptions BindFcmOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Notifications:Fcm");
        return new FcmOptions
        {
            ServiceAccountJson = section["ServiceAccountJson"] ?? string.Empty,
            ServiceAccountPath = section["ServiceAccountPath"] ?? string.Empty,
        };
    }
}
