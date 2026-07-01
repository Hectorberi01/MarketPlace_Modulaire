using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Catalog.Contracts.IntegrationEvents;
using Marketplace.Modules.Offers.Contracts.IntegrationEvents;
using Marketplace.Modules.Reviews.Contracts.IntegrationEvents;
using Marketplace.Modules.Search.Application.Abstractions;
using Marketplace.Modules.Search.Application.Indexing;
using Marketplace.Modules.Search.Application.Search;
using Marketplace.Modules.Search.Domain.Documents;
using Marketplace.Modules.Search.Infrastructure.Persistence;

namespace Marketplace.Modules.Search.Infrastructure;

/// <summary>Enregistre le module Search : DbContext read model, repository, indexeur et consumers d'events.</summary>
public sealed class SearchModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Search";

    public Assembly ApplicationAssembly => typeof(SearchProductsQuery).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<SearchDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", SearchDbContext.SchemaName)));

        services.AddScoped<ISearchUnitOfWork>(sp => sp.GetRequiredService<SearchDbContext>());

        services.AddScoped<ISearchDocumentRepository, SearchDocumentRepository>();
        services.AddScoped<SearchIndexer>();

        // Consumers : l'index est alimenté par les events des autres modules.
        services.AddScoped<IIntegrationEventHandler<ProductCreatedIntegrationEvent>, ProductCreatedSearchHandler>();
        services.AddScoped<IIntegrationEventHandler<ProductStatusChangedIntegrationEvent>, ProductStatusChangedSearchHandler>();
        services.AddScoped<IIntegrationEventHandler<ProductDeletedIntegrationEvent>, ProductDeletedSearchHandler>();
        services.AddScoped<IIntegrationEventHandler<OfferCreatedIntegrationEvent>, OfferCreatedSearchHandler>();
        services.AddScoped<IIntegrationEventHandler<OfferPriceChangedIntegrationEvent>, OfferPriceChangedSearchHandler>();
        services.AddScoped<IIntegrationEventHandler<ReviewPublishedIntegrationEvent>, ReviewPublishedSearchHandler>();
        services.AddScoped<IIntegrationEventHandler<ReviewRejectedIntegrationEvent>, ReviewRejectedSearchHandler>();

        services.AddOutboxProcessor<SearchDbContext>();
    }
}
