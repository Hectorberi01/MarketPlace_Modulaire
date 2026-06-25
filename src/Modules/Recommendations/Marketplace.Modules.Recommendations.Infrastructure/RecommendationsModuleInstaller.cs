using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.Modules.Recommendations.Application.Recommendations;
using Marketplace.Modules.Recommendations.Domain.Recommendations;
using Marketplace.Modules.Recommendations.Infrastructure.Persistence;

namespace Marketplace.Modules.Recommendations.Infrastructure;

/// <summary>Enregistre le module Recommendations : DbContext read model, repository, outbox.</summary>
public sealed class RecommendationsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Recommendations";

    public Assembly ApplicationAssembly => typeof(UpsertRecommendationCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<RecommendationsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", RecommendationsDbContext.SchemaName)));

        services.AddScoped<IRecommendationsUnitOfWork>(sp => sp.GetRequiredService<RecommendationsDbContext>());

        services.AddScoped<IRecommendationRepository, RecommendationRepository>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddHostedService<OutboxProcessor<RecommendationsDbContext>>();
    }
}
