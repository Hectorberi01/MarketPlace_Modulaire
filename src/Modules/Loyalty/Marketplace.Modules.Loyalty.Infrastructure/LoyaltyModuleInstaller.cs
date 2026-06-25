using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Loyalty.Application.Abstractions;
using Marketplace.Modules.Loyalty.Application.Accounts;
using Marketplace.Modules.Loyalty.Domain.Accounts;
using Marketplace.Modules.Loyalty.Infrastructure.Persistence;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Loyalty.Infrastructure;

/// <summary>Enregistre le module Loyalty : DbContext, repository, consumer de gains, validators, outbox.</summary>
public sealed class LoyaltyModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Loyalty";

    public Assembly ApplicationAssembly => typeof(RedeemPointsCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<LoyaltyDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", LoyaltyDbContext.SchemaName)));

        services.AddScoped<ILoyaltyUnitOfWork>(sp => sp.GetRequiredService<LoyaltyDbContext>());

        services.AddScoped<ILoyaltyAccountRepository, LoyaltyAccountRepository>();

        // Chorégraphie : crédit de points à la confirmation de commande.
        services.AddScoped<IIntegrationEventHandler<OrderConfirmedIntegrationEvent>, EarnPointsOnOrderConfirmedHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddHostedService<OutboxProcessor<LoyaltyDbContext>>();
    }
}
