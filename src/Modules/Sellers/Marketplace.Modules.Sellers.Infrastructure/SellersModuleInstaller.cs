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
using Marketplace.Modules.Reviews.Contracts.IntegrationEvents;
using Marketplace.Modules.Sellers.Application.Abstractions;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.RegisterSeller;
using Marketplace.Modules.Sellers.Application.Sellers.EventHandlers;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Sellers.Domain.Sellers;
using Marketplace.Modules.Sellers.Domain.Sellers.Events;
using Marketplace.Modules.Sellers.Infrastructure.Persistence;
using Marketplace.Modules.Sellers.Infrastructure.Public;
using Marketplace.Modules.Sellers.Infrastructure.Storage;

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

        // Stockage des pièces KYB : Cloudflare R2 (S3) si configuré, sinon stub hors-ligne.
        var r2Options = BindCloudflareR2Options(configuration);
        services.AddSingleton(r2Options);
        if (r2Options.IsConfigured)
        {
            services.AddHttpClient(CloudflareR2KybStorage.ClientName);
            services.AddScoped<IKybDocumentStorage, CloudflareR2KybStorage>();
        }
        else
        {
            services.AddScoped<IKybDocumentStorage, SimulatedKybStorage>();
        }

        services.AddScoped<IDomainEventHandler<SellerRegisteredDomainEvent>, SellerRegisteredDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerActivatedDomainEvent>, SellerActivatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerClosedDomainEvent>, SellerClosedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerReactivatedDomainEvent>, SellerReactivatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerDeletedDomainEvent>, SellerDeletedDomainEventHandler>();

        // Stats de vitrine, alimentées par d'autres modules (recalcul idempotent) :
        //   • ventes  ← commande confirmée (Ordering)
        //   • note    ← avis publié / rejeté (Reviews), produit → vendeur via Catalog
        services.AddScoped<SellerRatingRefresher>();
        services.AddScoped<IIntegrationEventHandler<OrderConfirmedIntegrationEvent>, RefreshSellerSalesOnOrderConfirmedHandler>();
        services.AddScoped<IIntegrationEventHandler<ReviewPublishedIntegrationEvent>, RefreshSellerRatingOnReviewPublishedHandler>();
        services.AddScoped<IIntegrationEventHandler<ReviewRejectedIntegrationEvent>, RefreshSellerRatingOnReviewRejectedHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<SellersDbContext>();
    }

    private static CloudflareR2Options BindCloudflareR2Options(IConfiguration configuration)
    {
        var section = configuration.GetSection("Storage:CloudflareR2");
        var options = new CloudflareR2Options
        {
            AccountId = section["AccountId"] ?? string.Empty,
            AccessKeyId = section["AccessKeyId"] ?? string.Empty,
            SecretAccessKey = section["SecretAccessKey"] ?? string.Empty,
            BucketName = section["BucketName"] ?? string.Empty,
            PublicBaseUrl = section["PublicBaseUrl"] ?? string.Empty
        };
        if (!string.IsNullOrWhiteSpace(section["Region"]))
        {
            options.Region = section["Region"]!;
        }
        return options;
    }
}
