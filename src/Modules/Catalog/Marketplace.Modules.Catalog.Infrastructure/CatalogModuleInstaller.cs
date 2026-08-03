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
using Marketplace.Modules.Sellers.Contracts.IntegrationEvents;
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

        // ─────────────────────────────────────────────────────────────────────────
        // TRAITEMENT D'IMAGE : UN CHOIX AU DÉMARRAGE, PAS UN REPLI À CHAUD.
        //
        // Les deux implémentations rendent le même service — détourage puis fond
        // blanc — et ne servent QU'À CELA : l'image finale part sur R2 par le flux de
        // création. rembg passe devant Cloudinary quand il est configuré : pas de
        // quota, pas de facture à l'usage, et les photos des vendeurs ne quittent pas
        // l'infrastructure.
        //
        // ⚠️ CE N'EST PAS UNE CHAÎNE DE SECOURS. L'adaptateur est choisi ICI, une fois,
        // et ne change plus. Si le conteneur rembg tombe, les détourages échouent —
        // ils NE BASCULENT PAS sur Cloudinary. C'est assumé : un basculement
        // silencieux vers un service payant, déclenché par une panne, est exactement
        // le genre de mécanisme qu'on découvre sur une facture.
        //
        // Pour revenir à Cloudinary, on vide `Media:Rembg:BaseUrl` et on redémarre.
        //
        // Sans aucun des deux, `NullImageProcessor` renvoie l'image inchangée. Il ne
        // se déclare PAS disponible (`IImageProcessingAvailability`), ce qui permet
        // aux interfaces de ne pas promettre un détourage qui n'aura pas lieu.
        // ─────────────────────────────────────────────────────────────────────────
        var rembg = BindRembgOptions(configuration);
        var cloudinary = BindCloudinaryOptions(configuration);
        services.AddSingleton(rembg);
        services.AddSingleton(cloudinary);

        if (rembg.IsConfigured)
        {
            services.AddHttpClient(RembgImageProcessor.ClientName, client =>
            {
                // Le délai est porté par un CancellationToken dans l'adaptateur, afin
                // de distinguer « trop lent » d'« annulé par l'appelant ». On laisse
                // néanmoins une borne large ici : un HttpClient sans limite garderait
                // une socket ouverte indéfiniment si le service se fige.
                client.Timeout = TimeSpan.FromSeconds(Math.Max(30, rembg.TimeoutSeconds) + 30);
            });
            // Santé partagée par tout le processus : une panne constatée par une
            // requête doit être connue de la suivante, et de l'endpoint de capacités.
            services.AddSingleton<RembgHealth>();
            services.AddScoped<IImageProcessor, RembgImageProcessor>();
        }
        else if (cloudinary.IsConfigured)
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

        // Handlers d'events d'intégration venus du module Sellers : Catalog réagit au
        // cycle de vie du compte vendeur (fermeture -> dépublication, suppression ->
        // archivage des produits).
        services.AddScoped<IIntegrationEventHandler<SellerClosedIntegrationEvent>, SellerClosedProductInvalidationHandler>();
        services.AddScoped<IIntegrationEventHandler<SellerDeletedIntegrationEvent>, SellerDeletedProductPurgeHandler>();

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

    /// <summary>
    /// Lie « Media:Rembg ». Absence de section = fonction inactive, jamais d'erreur au
    /// démarrage : une installation sans détourage doit rester une installation qui
    /// démarre.
    /// </summary>
    private static RembgOptions BindRembgOptions(IConfiguration configuration)
    {
        var section = configuration.GetSection("Media:Rembg");
        var options = new RembgOptions
        {
            BaseUrl = section["BaseUrl"] ?? string.Empty,
        };

        var model = section["Model"];
        if (!string.IsNullOrWhiteSpace(model))
        {
            options.Model = model.Trim();
        }

        if (int.TryParse(section["TimeoutSeconds"], out var timeout) && timeout > 0)
        {
            options.TimeoutSeconds = timeout;
        }

        if (int.TryParse(section["JpegQuality"], out var quality) && quality is > 0 and <= 100)
        {
            options.JpegQuality = quality;
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
