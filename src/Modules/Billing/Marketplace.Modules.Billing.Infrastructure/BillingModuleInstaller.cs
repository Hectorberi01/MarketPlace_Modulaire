using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.Modules.Billing.Application.Abstractions;
using Marketplace.Modules.Billing.Application.Commissions;
using Marketplace.Modules.Billing.Contracts;
using Marketplace.Modules.Billing.Domain.Commissions;
using Marketplace.Modules.Billing.Domain.Invoices;
using Marketplace.Modules.Billing.Infrastructure.Persistence;
using Marketplace.Modules.Billing.Infrastructure.Public;

namespace Marketplace.Modules.Billing.Infrastructure;

/// <summary>Enregistre le module Billing : DbContext, repositories, API commission, validators, outbox.</summary>
public sealed class BillingModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Billing";

    public Assembly ApplicationAssembly => typeof(CreateCommissionRuleCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // Taux de commission plateforme par défaut (section « Billing »), lié
        // manuellement (même pattern que Payments : aucune dépendance IOptions).
        var billingOptions = new BillingOptions();
        var rateRaw = configuration.GetSection("Billing")["DefaultCommissionRate"];
        if (decimal.TryParse(rateRaw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedRate))
        {
            billingOptions.DefaultCommissionRate = parsedRate;
        }
        services.AddSingleton(billingOptions);

        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<BillingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", BillingDbContext.SchemaName)));

        services.AddScoped<IBillingUnitOfWork>(sp => sp.GetRequiredService<BillingDbContext>());

        services.AddScoped<ICommissionRuleRepository, CommissionRuleRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<ICommissionModuleApi, CommissionModuleApi>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<BillingDbContext>();
    }
}
