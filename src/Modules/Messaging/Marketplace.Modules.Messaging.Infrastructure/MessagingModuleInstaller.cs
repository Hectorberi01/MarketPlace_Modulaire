using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.Modules.Messaging.Application.Abstractions;
using Marketplace.Modules.Messaging.Application.Conversations;
using Marketplace.Modules.Messaging.Application.Conversations.EventHandlers;
using Marketplace.Modules.Messaging.Domain.Conversations;
using Marketplace.Modules.Messaging.Domain.Conversations.Events;
using Marketplace.Modules.Messaging.Infrastructure.Persistence;

namespace Marketplace.Modules.Messaging.Infrastructure;

/// <summary>Enregistre le module Messaging : DbContext, repository, handlers, validators, outbox.</summary>
public sealed class MessagingModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Messaging";

    public Assembly ApplicationAssembly => typeof(StartConversationCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<MessagingDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", MessagingDbContext.SchemaName)));

        services.AddScoped<IMessagingUnitOfWork>(sp => sp.GetRequiredService<MessagingDbContext>());

        services.AddScoped<IConversationRepository, ConversationRepository>();

        services.AddScoped<IDomainEventHandler<MessageSentDomainEvent>, MessageSentDomainEventHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<MessagingDbContext>();
    }
}
