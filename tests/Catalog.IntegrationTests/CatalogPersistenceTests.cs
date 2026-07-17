using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Infrastructure;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Catalog.Domain.Products;
using Marketplace.Modules.Catalog.Infrastructure;
using Marketplace.Modules.Catalog.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Catalog.IntegrationTests;

/// <summary>
/// Test d'intégration de bout en bout du module Catalog sur un vrai PostgreSQL
/// (Testcontainers — Docker requis). Vérifie : migration, persistance produit,
/// et écriture de l'IntegrationEvent dans l'outbox dans la même transaction.
/// </summary>
public sealed class CatalogPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("marketplace")
        .Build();

    private ServiceProvider _services = default!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Marketplace"] = _postgres.GetConnectionString(),
                // Clé de test : le module Identity refuse de s'installer sans elle.
                ["Jwt:SigningKey"] = "test-only-signing-key-at-least-32-bytes-long!!"
            })
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddBuildingBlocksInfrastructure();
        new CatalogModuleInstaller().Install(serviceCollection, configuration);

        _services = serviceCollection.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Creer_un_produit_persiste_et_ecrit_loutbox()
    {
        var productId = await CreateProductAsync();

        await using var scope = _services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        var product = await dbContext.Products.FindAsync(new ProductId(productId));
        product.Should().NotBeNull();
        product!.Slug.Value.Should().Be("robe-wax");

        var outbox = await dbContext.OutboxMessages
            .Where(m => m.ProcessedOnUtc == null)
            .ToListAsync();

        outbox.Should().ContainSingle();
        outbox[0].Type.Should().Contain("ProductCreatedIntegrationEvent");
    }

    private async Task<Guid> CreateProductAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<ICatalogUnitOfWork>();

        var product = Product.Create(Guid.NewGuid(), Guid.NewGuid(), "Robe Wax", "Tissu africain").Value;
        await repository.AddAsync(product);
        await unitOfWork.SaveChangesAsync();

        return product.Id.Value;
    }
}
