using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.Api.Modules;
using Marketplace.BuildingBlocks.Application;
using Marketplace.BuildingBlocks.Infrastructure;
using Marketplace.Modules.Fraud.Contracts;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Tax.Contracts;
using Testcontainers.PostgreSql;
using Xunit;

namespace Saga.IntegrationTests;

/// <summary>
/// Test de composition de bout en bout : monte le graphe DI des 26 modules
/// (via ModuleRegistry, source unique de vérité du Bootstrap), applique TOUTES
/// les migrations sur un vrai PostgreSQL, et résout des APIs inter-modules.
///
/// C'est le filet anti-régression de la composition : il échoue dès qu'un
/// installer, un modèle EF, une migration ou un enregistrement DI est cassé —
/// la classe d'erreurs la plus coûteuse à diagnostiquer au démarrage.
/// </summary>
public sealed class FullCompositionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("marketplace")
        .Build();

    private ServiceProvider _services = default!;
    private readonly List<Type> _dbContextTypes = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Marketplace"] = _postgres.GetConnectionString(),

                // Le module Identity REFUSE de s'installer sans clé de signature —
                // c'est voulu : une clé absente en production signifierait des jetons
                // signés avec un secret vide. Ce test compose les 26 modules, donc il
                // doit fournir la clé comme le ferait un vrai hôte.
                //
                // Clé de test, jamais déployée. La longueur compte : HMAC-SHA256 exige
                // au moins 32 octets, et un secret plus court fait lever la
                // bibliothèque, pas notre code.
                ["Jwt:SigningKey"] = "test-only-signing-key-at-least-32-bytes-long!!",

                // ─────────────────────────────────────────────────────────────
                // MÊME RAISON QUE LA CLÉ JWT : le module Deliveries REFUSE de
                // s'installer sans Redis hors développement.
                //
                // Ce n'est pas une contrainte à contourner, c'est la garde qui
                // empêche de déployer un dispatch cassé : les positions des
                // livreurs vivent dans Redis, et deux instances sans cache
                // partagé ne verraient chacune qu'une partie de la flotte.
                //
                // Ce test compose les 26 modules SANS Redis — il déclare donc
                // qu'il est un environnement de développement, ce qu'il est. On
                // ne relâche PAS la garde pour lui : la relâcher ferait passer le
                // test le jour où la production démarrerait sans Redis.
                //
                // Les DEUX clés sont posées : un hôte web lit
                // « ASPNETCORE_ENVIRONMENT », un hôte générique — ce test en est
                // un — lit « DOTNET_ENVIRONMENT ».
                // ─────────────────────────────────────────────────────────────
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["DOTNET_ENVIRONMENT"] = "Development"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Composition identique au Bootstrap (sans la couche web).
        foreach (var module in ModuleRegistry.Installers)
        {
            module.Install(services, configuration);
        }

        services.AddMediatR(cfg =>
        {
            foreach (var assembly in ModuleRegistry.Installers.Select(i => i.ApplicationAssembly).Distinct())
            {
                cfg.RegisterServicesFromAssembly(assembly);
            }
        });

        services.AddBuildingBlocksPipeline();
        services.AddBuildingBlocksInfrastructure();

        // Tous les DbContext enregistrés (un par module) pour migration.
        _dbContextTypes.AddRange(services
            .Where(d => d.ServiceType is { IsClass: true } t && t != typeof(DbContext) && typeof(DbContext).IsAssignableFrom(t))
            .Select(d => d.ServiceType)
            .Distinct());

        _services = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// Nombre de modules composés. 25 marchands + HBA Delivery.
    ///
    /// ⚠️ CE NOMBRE EN DUR EST UNE SENTINELLE, PAS UNE CORVÉE.
    ///
    /// Il ne sert pas à compter : il sert à faire échouer la construction le jour
    /// où un module DISPARAÎT du registre. Un module retiré par erreur d'un
    /// ModuleRegistry ne casse rien d'autre — l'application démarre, ses routes
    /// répondent 404, et personne ne s'en aperçoit avant qu'un client ne réclame.
    ///
    /// Le remplacer par « _dbContextTypes.Should().NotBeEmpty() » supprimerait ce
    /// travail de mise à jour, et avec lui la seule chose que ce test protège.
    /// </summary>
    private const int ModulesComposes = 26;

    [Fact]
    public void Chaque_module_expose_son_DbContext()
        => _dbContextTypes.Should().HaveCount(ModulesComposes);

    [Fact]
    public async Task Toutes_les_migrations_sappliquent_sans_erreur()
    {
        await using var scope = _services.CreateAsyncScope();

        foreach (var contextType in _dbContextTypes)
        {
            var dbContext = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            await dbContext.Database.MigrateAsync();
        }
    }

    [Fact]
    public void Les_APIs_inter_modules_se_resolvent()
    {
        using var scope = _services.CreateScope();

        scope.ServiceProvider.GetService<IOrderingModuleApi>().Should().NotBeNull();
        scope.ServiceProvider.GetService<ITaxModuleApi>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IFraudModuleApi>().Should().NotBeNull();
    }
}
