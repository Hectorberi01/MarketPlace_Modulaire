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
using Marketplace.Modules.Shipping.Application.Abstractions;
using Marketplace.Modules.Shipping.Application.Shipments.Commands;
using Marketplace.Modules.Shipping.Application.Shipments.EventHandlers;
using Marketplace.Modules.Shipping.Contracts;
using Marketplace.Modules.Shipping.Domain.Carriers;
using Marketplace.Modules.Shipping.Domain.Shipments;
using Marketplace.Modules.Shipping.Domain.Shipments.Events;
using Marketplace.Modules.Shipping.Domain.Zones;
using Marketplace.Modules.Shipping.Infrastructure.Persistence;
using Marketplace.Modules.Shipping.Infrastructure.Public;

namespace Marketplace.Modules.Shipping.Infrastructure;

/// <summary>Enregistre le module Shipping : DbContext, repository, API publique, handlers, validators, outbox.</summary>
public sealed class ShippingModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Shipping";

    public Assembly ApplicationAssembly => typeof(MarkShipmentShippedCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<ShippingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", ShippingDbContext.SchemaName)));

        services.AddScoped<IShippingUnitOfWork>(sp => sp.GetRequiredService<ShippingDbContext>());

        services.AddScoped<IShipmentRepository, ShipmentRepository>();
        services.AddScoped<ICarrierRepository, CarrierRepository>();

        // Grille tarifaire par zone. Lue à chaque devis de livraison, donc sur le
        // chemin critique du tunnel de commande.
        services.AddScoped<IShippingZoneRepository, ShippingZoneRepository>();
        services.AddScoped<IShippingModuleApi, ShippingModuleApi>();

        services.AddScoped<IDomainEventHandler<ShipmentShippedDomainEvent>, ShipmentShippedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<ShipmentDeliveredDomainEvent>, ShipmentDeliveredDomainEventHandler>();

        // DÉCLENCHEUR DE LA LIVRAISON PAR COURSIER : la commande devient ramassable
        // quand la DERNIÈRE part vendeur passe en préparation.
        services.AddScoped<IDomainEventHandler<ShipmentPreparingDomainEvent>, PublishOrderReadyForDeliveryHandler>();

        // Chorégraphie : création des expéditions à la confirmation de commande.
        services.AddScoped<IIntegrationEventHandler<OrderConfirmedIntegrationEvent>, CreateShipmentsOnOrderConfirmedHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<ShippingDbContext>();
    }
}
