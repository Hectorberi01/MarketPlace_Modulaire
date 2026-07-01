using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.Modules.Inventory.Application.Abstractions;
using Marketplace.Modules.Inventory.Application.Stock.Commands;
using Marketplace.Modules.Inventory.Application.Stock.EventHandlers;
using Marketplace.Modules.Inventory.Contracts;
using Marketplace.Modules.Inventory.Domain.Locations;
using Marketplace.Modules.Inventory.Domain.Stock;
using Marketplace.Modules.Inventory.Domain.Stock.Events;
using Marketplace.Modules.Inventory.Infrastructure.Persistence;
using Marketplace.Modules.Inventory.Infrastructure.Public;

namespace Marketplace.Modules.Inventory.Infrastructure;

/// <summary>Enregistre le module Inventory : DbContext, repositories, API publique, handlers, validators, outbox.</summary>
public sealed class InventoryModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Inventory";

    public Assembly ApplicationAssembly => typeof(ReserveStockCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<InventoryDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", InventoryDbContext.SchemaName)));

        services.AddScoped<IInventoryUnitOfWork>(sp => sp.GetRequiredService<InventoryDbContext>());

        services.AddScoped<IInventoryItemRepository, InventoryItemRepository>();
        services.AddScoped<IFulfillmentLocationRepository, FulfillmentLocationRepository>();
        services.AddScoped<IInventoryModuleApi, InventoryModuleApi>();

        services.AddScoped<IDomainEventHandler<StockReservedDomainEvent>, StockReservedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<StockDepletedDomainEvent>, StockDepletedDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<InventoryDbContext>();
    }
}
