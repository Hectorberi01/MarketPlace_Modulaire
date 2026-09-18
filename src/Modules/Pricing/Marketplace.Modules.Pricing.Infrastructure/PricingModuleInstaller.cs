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
using Marketplace.Modules.Pricing.Application.Abstractions;
using Marketplace.Modules.Pricing.Application.Promotions.Commands.CreatePromotion;
using Marketplace.Modules.Pricing.Application.Promotions.EventHandlers;
using Marketplace.Modules.Pricing.Contracts;
using Marketplace.Modules.Pricing.Domain.Promotions;
using Marketplace.Modules.Pricing.Domain.Promotions.Events;
using Marketplace.Modules.Pricing.Infrastructure.Persistence;
using Marketplace.Modules.Pricing.Infrastructure.Public;

namespace Marketplace.Modules.Pricing.Infrastructure;

/// <summary>Enregistre le module Pricing : DbContext, repository, API publique, handlers, validators, outbox.</summary>
public sealed class PricingModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Pricing";

    public Assembly ApplicationAssembly => typeof(CreatePromotionCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<PricingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", PricingDbContext.SchemaName)));

        services.AddScoped<IPricingUnitOfWork>(sp => sp.GetRequiredService<PricingDbContext>());

        services.AddScoped<IPromotionRepository, PromotionRepository>();
        services.AddScoped<IPromotionRedemptionRepository, PromotionRedemptionRepository>();
        services.AddScoped<IPricingModuleApi, PricingModuleApi>();

        services.AddScoped<IDomainEventHandler<PromotionCreatedDomainEvent>, PromotionCreatedDomainEventHandler>();

        // SANS CETTE LIGNE, AUCUN COUPON N'EST JAMAIS DÉCOMPTÉ.
        //
        // MediatR et le dispatcher d'événements d'intégration résolvent LAZILY : un
        // handler non enregistré ne provoque aucune erreur au démarrage. Il n'est
        // simplement jamais appelé — en silence. C'est précisément ainsi que
        // `RecordUsage()` a pu rester sans appelant sans que rien ne le signale.
        services.AddScoped<IIntegrationEventHandler<OrderConfirmedIntegrationEvent>, RedeemPromotionOnOrderConfirmedHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<PricingDbContext>();
    }
}
