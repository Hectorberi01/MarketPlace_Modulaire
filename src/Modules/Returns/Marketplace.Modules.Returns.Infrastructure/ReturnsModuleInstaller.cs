using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.Modules.Returns.Application.Abstractions;
using Marketplace.Modules.Returns.Application.Returns;
using Marketplace.Modules.Returns.Application.Returns.EventHandlers;
using Marketplace.Modules.Returns.Domain.Returns;
using Marketplace.Modules.Returns.Domain.Returns.Events;
using Marketplace.Modules.Returns.Infrastructure.Persistence;

namespace Marketplace.Modules.Returns.Infrastructure;

/// <summary>Enregistre le module Returns : DbContext, repository, handlers, validators, outbox.</summary>
public sealed class ReturnsModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Returns";

    public Assembly ApplicationAssembly => typeof(RequestReturnCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<ReturnsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", ReturnsDbContext.SchemaName)));

        services.AddScoped<IReturnsUnitOfWork>(sp => sp.GetRequiredService<ReturnsDbContext>());

        services.AddScoped<IReturnRequestRepository, ReturnRequestRepository>();

        services.AddScoped<IDomainEventHandler<ReturnRefundedDomainEvent>, ReturnRefundedDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddHostedService<OutboxProcessor<ReturnsDbContext>>();
    }
}
