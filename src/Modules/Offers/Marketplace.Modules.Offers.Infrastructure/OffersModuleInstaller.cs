using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.Modules.Offers.Application.Abstractions;
using Marketplace.Modules.Offers.Application.Offers;
using Marketplace.Modules.Offers.Application.Offers.Commands.CreateOffer;
using Marketplace.Modules.Offers.Application.Offers.EventHandlers;
using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Offers.Domain.Offers;
using Marketplace.Modules.Offers.Domain.Offers.Events;
using Marketplace.Modules.Offers.Infrastructure.Persistence;
using Marketplace.Modules.Offers.Infrastructure.Public;

namespace Marketplace.Modules.Offers.Infrastructure;

/// <summary>Enregistre le module Offers : DbContext, repository, API publique, handlers, validators, outbox.</summary>
public sealed class OffersModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Offers";

    public Assembly ApplicationAssembly => typeof(CreateOfferCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // Taux du modèle de prix (section « Pricing »), liés manuellement
        // (même pattern que Payments/Billing : aucune dépendance IOptions).
        var pricingOptions = new PricingOptions();
        var pricingSection = configuration.GetSection("Pricing");

        var commRaw = pricingSection["PlatformCommissionRate"];
        if (decimal.TryParse(commRaw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedComm))
        {
            pricingOptions.PlatformCommissionRate = parsedComm;
        }

        var provRaw = pricingSection["ProviderFeeRate"];
        if (decimal.TryParse(provRaw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedProv))
        {
            pricingOptions.ProviderFeeRate = parsedProv;
        }

        services.AddSingleton(pricingOptions);

        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<OffersDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", OffersDbContext.SchemaName)));

        services.AddScoped<IOffersUnitOfWork>(sp => sp.GetRequiredService<OffersDbContext>());

        services.AddScoped<IOfferRepository, OfferRepository>();
        services.AddScoped<IOfferModuleApi, OfferModuleApi>();

        services.AddScoped<IDomainEventHandler<OfferCreatedDomainEvent>, OfferCreatedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<OfferPriceChangedDomainEvent>, OfferPriceChangedDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<OffersDbContext>();
    }
}
