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

        // Stockage des pièces KYB : service S3-compatible si configuré (R2 ou MinIO),
        // sinon stub hors-ligne.
        var kybStorageOptions = BindKybStorageOptions(configuration);
        services.AddSingleton(kybStorageOptions);
        if (kybStorageOptions.IsConfigured)
        {
            services.AddHttpClient(S3KybStorage.ClientName);
            services.AddScoped<IKybDocumentStorage, S3KybStorage>();
        }
        else
        {
            services.AddScoped<IKybDocumentStorage, SimulatedKybStorage>();
        }

        services.AddScoped<IDomainEventHandler<SellerRegisteredDomainEvent>, SellerRegisteredDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerActivatedDomainEvent>, SellerActivatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerClosedDomainEvent>, SellerClosedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerKybRejectedDomainEvent>, SellerKybRejectedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerSuspendedDomainEvent>, SellerSuspendedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<SellerSuspensionLiftedDomainEvent>, SellerSuspensionLiftedDomainEventHandler>();
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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// DEUX SECTIONS DE CONFIGURATION, ET LA NOUVELLE L'EMPORTE.
    ///
    /// « Storage:KybStorage » est la section à utiliser désormais : elle ne nomme
    /// plus un fournisseur, et elle accepte `ServiceUrl` pour viser un MinIO.
    ///
    /// « Storage:CloudflareR2 » reste lue en repli. Ce n'est pas de la complaisance :
    /// c'est ce nom-là qui est écrit dans les `.env` des trois environnements et dans
    /// `env.j2`. Le retirer d'un coup aurait fait basculer la production sur le stub
    /// hors-ligne — c'est-à-dire des pièces KYB acceptées et jamais stockées, sans
    /// erreur visible. La bascule se fait donc fichier par fichier, et cette méthode
    /// disparaîtra quand le dernier aura été migré.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static S3StorageOptions BindKybStorageOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Storage:KybStorage");
        var legacy = configuration.GetSection("Storage:CloudflareR2");

        string Read(string key) =>
            section[key] is { Length: > 0 } value ? value : legacy[key] ?? string.Empty;

        var options = new S3StorageOptions
        {
            ServiceUrl = Read("ServiceUrl"),
            AccountId = Read("AccountId"),
            AccessKeyId = Read("AccessKeyId"),
            SecretAccessKey = Read("SecretAccessKey"),
            BucketName = Read("BucketName"),
            // PAS de repli sur la valeur historique : sur un bucket KYB, une base
            // publique renseignée rendrait des pièces d'identité lisibles par qui
            // connaît le nom du fichier. Si elle doit exister, elle doit être posée
            // explicitement dans la nouvelle section.
            PublicBaseUrl = section["PublicBaseUrl"] ?? string.Empty
        };

        var region = Read("Region");
        if (!string.IsNullOrWhiteSpace(region))
        {
            options.Region = region;
        }

        return options;
    }
}
