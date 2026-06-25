using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.Modules.Fraud.Application.Risk;
using Marketplace.Modules.Fraud.Contracts;
using Marketplace.Modules.Fraud.Domain.Risk;
using Marketplace.Modules.Fraud.Infrastructure.Persistence;
using Marketplace.Modules.Fraud.Infrastructure.Public;

namespace Marketplace.Modules.Fraud.Infrastructure;

/// <summary>Enregistre le module Fraud : DbContext, repository, API de décision, validators, outbox.</summary>
public sealed class FraudModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Fraud";

    public Assembly ApplicationAssembly => typeof(AssessRiskCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<FraudDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", FraudDbContext.SchemaName)));

        services.AddScoped<IFraudUnitOfWork>(sp => sp.GetRequiredService<FraudDbContext>());

        services.AddScoped<IRiskAssessmentRepository, RiskAssessmentRepository>();
        services.AddScoped<IFraudModuleApi, FraudModuleApi>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddHostedService<OutboxProcessor<FraudDbContext>>();
    }
}
