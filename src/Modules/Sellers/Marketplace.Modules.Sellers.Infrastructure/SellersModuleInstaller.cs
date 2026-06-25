using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.Modules.Sellers.Application.Abstractions;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.RegisterSeller;
using Marketplace.Modules.Sellers.Application.Sellers.EventHandlers;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Sellers.Domain.Sellers;
using Marketplace.Modules.Sellers.Domain.Sellers.Events;
using Marketplace.Modules.Sellers.Infrastructure.Persistence;
using Marketplace.Modules.Sellers.Infrastructure.Public;

namespace Marketplace.Modules.Sellers.Infrastructure;

/// <summary>
/// Enregistre tout le module Sellers : DbContext (schéma propre), repository, API
/// publique, handlers d'events, validators, processeur d'outbox.
/// </summary>
public sealed class SellersModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Sellers";

    public Assembly ApplicationAssembly => typeof(RegisterSellerCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<SellersDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", SellersDbContext.SchemaName)));

        services.AddScoped<ISellerUnitOfWork>(sp => sp.GetRequiredService<SellersDbContext>());

        services.AddScoped<ISellerRepository, SellerRepository>();
        services.AddScoped<ISellerModuleApi, SellerModuleApi>();

        services.AddScoped<IDomainEventHandler<SellerRegisteredDomainEvent>, SellerRegisteredDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerActivatedDomainEvent>, SellerActivatedDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddHostedService<OutboxProcessor<SellersDbContext>>();
    }
}
