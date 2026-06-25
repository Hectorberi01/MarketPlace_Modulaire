using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Brands.EventHandlers;
using Marketplace.Modules.Catalog.Application.Categories.EventHandlers;
using Marketplace.Modules.Catalog.Application.Products.Commands.CreateProduct;
using Marketplace.Modules.Catalog.Application.Products.EventHandlers;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Catalog.Domain.Brands;
using Marketplace.Modules.Catalog.Domain.Brands.Events;
using Marketplace.Modules.Catalog.Domain.Categories;
using Marketplace.Modules.Catalog.Domain.Categories.Events;
using Marketplace.Modules.Catalog.Domain.Products;
using Marketplace.Modules.Catalog.Domain.Products.Events;
using Marketplace.Modules.Catalog.Infrastructure.Persistence;
using Marketplace.Modules.Catalog.Infrastructure.Public;

namespace Marketplace.Modules.Catalog.Infrastructure;

/// <summary>
/// Enregistre tout le module Catalog : DbContext (schéma propre), repositories,
/// API publique, handlers d'events, validators, processeur d'outbox. Le
/// Bootstrap se contente d'appeler cet installer — il ne connaît pas les
/// internes du module.
/// </summary>
public sealed class CatalogModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Catalog";

    public Assembly ApplicationAssembly => typeof(CreateProductCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<CatalogDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", CatalogDbContext.SchemaName)));

        // Unit of Work propre au module (interface dédiée -> pas de collision DI).
        services.AddScoped<ICatalogUnitOfWork>(sp => sp.GetRequiredService<CatalogDbContext>());

        // Ports du module.
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IBrandRepository, BrandRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<ICatalogModuleApi, CatalogModuleApi>();

        // Handlers de domain events (résolus par le DomainEventDispatcher).
        services.AddScoped<IDomainEventHandler<ProductCreatedDomainEvent>, ProductCreatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<BrandCreatedDomainEvent>, BrandCreatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<CategoryCreatedDomainEvent>, CategoryCreatedDomainEventHandler>();

        // Validators FluentValidation du module.
        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        // Processeur d'outbox dédié au DbContext du module.
        services.AddHostedService<OutboxProcessor<CatalogDbContext>>();
    }
}
