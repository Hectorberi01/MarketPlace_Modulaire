using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.Modules.Delivery.Application.Abstractions;
using Marketplace.Modules.Delivery.Application.Couriers;
using Marketplace.Modules.Delivery.Contracts;
using Marketplace.Modules.Delivery.Domain.Couriers;
using Marketplace.Modules.Delivery.Domain.Couriers.Events;
using Marketplace.Modules.Delivery.Infrastructure.Persistence;

namespace Marketplace.Modules.Delivery.Infrastructure;

/// <summary>
/// Enregistre le module Delivery : DbContext, repository, API publique, handlers, outbox.
///
/// Ce module est VOLONTAIREMENT MINCE. Il porte l'identité du coursier côté marketplace
/// — de quoi le payer et de quoi refuser de le payer. Le dispatch, la géolocalisation et
/// l'application coursier vivent dans le service autonome décrit par
/// docs/CDC_Service_Livraison_Coursier.md, et n'ont rien à faire ici.
/// </summary>
public sealed class DeliveryModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Delivery";

    public Assembly ApplicationAssembly => typeof(RegisterCourierCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<DeliveryDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", DeliveryDbContext.SchemaName)));

        services.AddScoped<IDeliveryUnitOfWork>(sp => sp.GetRequiredService<DeliveryDbContext>());

        services.AddScoped<ICourierRepository, CourierRepository>();
        services.AddScoped<IDeliveryModuleApi, Public.DeliveryModuleApi>();

        services.AddScoped<IDomainEventHandler<CourierActivatedDomainEvent>, CourierActivatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<CourierSuspendedDomainEvent>, CourierSuspendedDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<DeliveryDbContext>();
    }
}
