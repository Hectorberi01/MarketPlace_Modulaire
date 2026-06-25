using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.Modules.Tax.Application.Abstractions;
using Marketplace.Modules.Tax.Application.Taxes;
using Marketplace.Modules.Tax.Contracts;
using Marketplace.Modules.Tax.Domain.Taxes;
using Marketplace.Modules.Tax.Infrastructure.Persistence;
using Marketplace.Modules.Tax.Infrastructure.Public;

namespace Marketplace.Modules.Tax.Infrastructure;

/// <summary>Enregistre le module Tax : DbContext, repository, API de calcul, validators, outbox.</summary>
public sealed class TaxModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Tax";

    public Assembly ApplicationAssembly => typeof(CreateTaxRuleCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<TaxDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", TaxDbContext.SchemaName)));

        services.AddScoped<ITaxUnitOfWork>(sp => sp.GetRequiredService<TaxDbContext>());

        services.AddScoped<ITaxRuleRepository, TaxRuleRepository>();
        services.AddScoped<ITaxModuleApi, TaxModuleApi>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddHostedService<OutboxProcessor<TaxDbContext>>();
    }
}
