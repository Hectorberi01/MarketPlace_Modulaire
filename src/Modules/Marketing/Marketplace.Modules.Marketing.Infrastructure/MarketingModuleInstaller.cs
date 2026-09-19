using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.Modules.Marketing.Application.Abstractions;
using Marketplace.Modules.Marketing.Application.Campaigns;
using Marketplace.Modules.Marketing.Domain.Campaigns;
using Marketplace.Modules.Marketing.Domain.Content;
using Marketplace.Modules.Marketing.Domain.Legal;
using Marketplace.Modules.Marketing.Infrastructure.Persistence;

namespace Marketplace.Modules.Marketing.Infrastructure;

/// <summary>Enregistre le module Marketing : DbContext, repositories, validators, outbox.</summary>
public sealed class MarketingModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Marketing";

    public Assembly ApplicationAssembly => typeof(CreateCampaignCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<MarketingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", MarketingDbContext.SchemaName)));

        services.AddScoped<IMarketingUnitOfWork>(sp => sp.GetRequiredService<MarketingDbContext>());

        services.AddScoped<ICampaignRepository, CampaignRepository>();
        services.AddScoped<IContentBlockRepository, ContentBlockRepository>();
        services.AddScoped<ILegalDocumentRepository, LegalDocumentRepository>();
        services.AddScoped<ILegalAcceptanceRepository, LegalAcceptanceRepository>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<MarketingDbContext>();
    }
}
