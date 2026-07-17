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
using Marketplace.Modules.Catalog.Infrastructure.Media;
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

        // Service média externe (https://hbamediacore.fr). Stub hors-ligne tant que
        // « Media:ClientId » n'est pas renseigné ; adaptateur HTTP réel sinon.
        // La config ne SURCHARGE que si elle fournit une valeur non vide : on
        // conserve sinon les défauts de MediaServiceOptions (dont le ClientId réel),
        // pour éviter qu'un « ClientId: "" » d'appsettings ne désactive l'upload.
        var mediaOptions = new MediaServiceOptions();
        if (!string.IsNullOrWhiteSpace(configuration["Media:BaseUrl"]))
        {
            mediaOptions.BaseUrl = configuration["Media:BaseUrl"]!;
        }
        if (!string.IsNullOrWhiteSpace(configuration["Media:ClientId"]))
        {
            mediaOptions.ClientId = configuration["Media:ClientId"]!;
        }
        if (!string.IsNullOrWhiteSpace(configuration["Media:DefaultVisibility"]))
        {
            mediaOptions.DefaultVisibility = configuration["Media:DefaultVisibility"]!;
        }
        services.AddSingleton(mediaOptions);

        // Priorité au stockage Cloudflare R2 (S3) pour les images produit s'il est
        // configuré : upload PUBLIC + suppression (retrait d'image / suppression de
        // produit efface le fichier). Sinon HBA Media Core, sinon stub hors-ligne.
        var r2Media = BindR2MediaOptions(configuration);
        services.AddSingleton(r2Media);

        if (r2Media.IsConfigured)
        {
            services.AddHttpClient(CloudflareR2MediaStorage.ClientName);
            services.AddScoped<IMediaStorage, CloudflareR2MediaStorage>();
        }
        else if (mediaOptions.IsConfigured)
        {
            services.AddHttpClient(HbaMediaCoreStorage.ClientName, client =>
                client.BaseAddress = new Uri(EnsureTrailingSlash(mediaOptions.BaseUrl)));
            services.AddScoped<IMediaStorage, HbaMediaCoreStorage>();
        }
        else
        {
            services.AddScoped<IMediaStorage, SimulatedMediaStorage>();
        }

        // Traitement d'image (détourage IA + fond blanc) via Cloudinary s'il est
        // configuré. Cloudinary ne sert qu'au traitement : l'image finale est
        // rapatriée dans R2 par le flux de création. Sinon, no-op (image inchangée).
        var cloudinary = BindCloudinaryOptions(configuration);
        services.AddSingleton(cloudinary);
        if (cloudinary.IsConfigured)
        {
            services.AddHttpClient(CloudinaryImageProcessor.ClientName);
            services.AddScoped<IImageProcessor, CloudinaryImageProcessor>();
        }
        else
        {
            services.AddScoped<IImageProcessor, NullImageProcessor>();
        }

        // Handlers de domain events (résolus par le DomainEventDispatcher).
        services.AddScoped<IDomainEventHandler<ProductCreatedDomainEvent>, ProductCreatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<ProductStatusChangedDomainEvent>, ProductStatusChangedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<BrandCreatedDomainEvent>, BrandCreatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<CategoryCreatedDomainEvent>, CategoryCreatedDomainEventHandler>();

        // Validators FluentValidation du module.
        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        // Processeur d'outbox dédié au DbContext du module.
        services.AddOutboxProcessor<CatalogDbContext>();
    }

    private static string EnsureTrailingSlash(string url)
        => url.EndsWith('/') ? url : url + "/";

    private static CloudflareR2MediaOptions BindR2MediaOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Storage:CloudflareR2Media");
        var options = new CloudflareR2MediaOptions
        {
            AccountId = section["AccountId"] ?? string.Empty,
            AccessKeyId = section["AccessKeyId"] ?? string.Empty,
            SecretAccessKey = section["SecretAccessKey"] ?? string.Empty,
            BucketName = section["BucketName"] ?? string.Empty,
            PublicBaseUrl = section["PublicBaseUrl"] ?? string.Empty,
            KeyPrefix = section["KeyPrefix"] ?? string.Empty
        };
        if (!string.IsNullOrWhiteSpace(section["Region"]))
        {
            options.Region = section["Region"]!;
        }
        return options;
    }

    private static CloudinaryOptions BindCloudinaryOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Media:Cloudinary");
        var options = new CloudinaryOptions
        {
            CloudName = section["CloudName"] ?? string.Empty,
            ApiKey = section["ApiKey"] ?? string.Empty,
            ApiSecret = section["ApiSecret"] ?? string.Empty,
        };
        if (int.TryParse(section["MaxWaitSeconds"], out var wait) && wait > 0)
        {
            options.MaxWaitSeconds = wait;
        }
        return options;
    }
}
