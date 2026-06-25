using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.Modules.Disputes.Application.Abstractions;
using Marketplace.Modules.Disputes.Application.Disputes;
using Marketplace.Modules.Disputes.Application.Disputes.EventHandlers;
using Marketplace.Modules.Disputes.Domain.Disputes;
using Marketplace.Modules.Disputes.Domain.Disputes.Events;
using Marketplace.Modules.Disputes.Infrastructure.Persistence;

namespace Marketplace.Modules.Disputes.Infrastructure;

/// <summary>Enregistre le module Disputes : DbContext, repository, handlers, validators, outbox.</summary>
public sealed class DisputesModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Disputes";

    public Assembly ApplicationAssembly => typeof(OpenDisputeCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<DisputesDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", DisputesDbContext.SchemaName)));

        services.AddScoped<IDisputesUnitOfWork>(sp => sp.GetRequiredService<DisputesDbContext>());

        services.AddScoped<IDisputeRepository, DisputeRepository>();

        services.AddScoped<IDomainEventHandler<DisputeOpenedDomainEvent>, DisputeOpenedDomainEventHandler>();
        services.AddScoped<IDomainEventHandler<DisputeResolvedDomainEvent>, DisputeResolvedDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddHostedService<OutboxProcessor<DisputesDbContext>>();
    }
}
