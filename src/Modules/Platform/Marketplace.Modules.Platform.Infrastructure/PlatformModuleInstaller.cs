using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.Modules.Platform.Application.Abstractions;
using Marketplace.Modules.Platform.Application.Providers;
using Marketplace.Modules.Platform.Contracts;
using Marketplace.Modules.Platform.Domain.Providers;
using Marketplace.Modules.Platform.Infrastructure.Persistence;
using Marketplace.Modules.Platform.Infrastructure.Public;
using Marketplace.Modules.Platform.Infrastructure.Security;

namespace Marketplace.Modules.Platform.Infrastructure;

/// <summary>
/// Enregistre le module Platform : registre des fournisseurs (paiement, e-mail, SMS,
/// WhatsApp, push), chiffrement de leurs secrets, journal des changements.
/// </summary>
public sealed class PlatformModuleInstaller : IModuleInstaller
{
    /// <summary>
    /// Clé de développement, PUBLIQUE et constante. Elle n'est utilisée que hors
    /// production, et la garde ci-dessous la refuse en production — comme JwtKeyGuard
    /// refuse la clé de signature committée.
    /// </summary>
    private const string DevelopmentKey = "REVWLU9OTFktUExBVEZPUk0tS0VZLU5PVC1TRUNSRVQ=";

    public string ModuleName => "Platform";

    public Assembly ApplicationAssembly => typeof(RegisterProviderCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<PlatformDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", PlatformDbContext.SchemaName)));

        services.AddScoped<IPlatformUnitOfWork>(sp => sp.GetRequiredService<PlatformDbContext>());
        services.AddScoped<IProviderRepository, ProviderRepository>();

        // Singleton : la clé maître est lue UNE fois, au démarrage. C'est aussi ce qui
        // fait échouer le démarrage, et non le premier paiement, quand elle manque.
        services.AddSingleton(BuildProtector(configuration));

        // Une seule instance derrière deux ports : le registre connaît le format de
        // ses clés de cache, donc c'est lui qui sait les purger.
        services.AddScoped<PlatformProviderRegistry>();
        services.AddScoped<IPlatformProviderRegistry>(sp => sp.GetRequiredService<PlatformProviderRegistry>());
        services.AddScoped<IProviderCacheInvalidator>(sp => sp.GetRequiredService<PlatformProviderRegistry>());

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<PlatformDbContext>();
    }

    /// <summary>
    /// Construit le chiffreur, ou REFUSE DE DÉMARRER.
    ///
    /// L'alternative tentante — pas de clé, on stocke en clair — ferait qu'un
    /// déploiement ayant oublié la variable écrirait les clés d'API des prestataires
    /// de paiement en texte dans la base, sans erreur et sans symptôme. Le trou ne se
    /// découvrirait qu'à la première fuite de sauvegarde. Un démarrage refusé se
    /// répare en cinq minutes.
    /// </summary>
    private static ISecretProtector BuildProtector(IConfiguration configuration)
    {
        var production = IsProduction(configuration);
        var brut = configuration["Platform:SecretKey"];

        if (string.IsNullOrWhiteSpace(brut))
        {
            if (production)
            {
                throw new InvalidOperationException(
                    "Platform:SecretKey est absente. Les secrets des fournisseurs ne peuvent pas être chiffrés. "
                    + "Générer la clé avec « openssl rand -base64 32 » et la placer dans le vault "
                    + "(PLATFORM_SECRET_KEY).");
            }

            brut = DevelopmentKey;
        }

        if (production && string.Equals(brut, DevelopmentKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Platform:SecretKey porte la clé de DÉVELOPPEMENT, qui est publique. "
                + "Quiconque lit le dépôt déchiffrerait les secrets des prestataires de paiement.");
        }

        byte[] cle;
        try
        {
            cle = Convert.FromBase64String(brut);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "Platform:SecretKey n'est pas du base64. Attendu : « openssl rand -base64 32 ».");
        }

        if (cle.Length != 32)
        {
            throw new InvalidOperationException(
                $"Platform:SecretKey fait {cle.Length} octets au lieu de 32 (AES-256). "
                + "Attendu : « openssl rand -base64 32 ».");
        }

        return new AesGcmSecretProtector(cle);
    }

    private static bool IsProduction(IConfiguration configuration)
    {
        var environment = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? string.Empty;

        return string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase);
    }
}
