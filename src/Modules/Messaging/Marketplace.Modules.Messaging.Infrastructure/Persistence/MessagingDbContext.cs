using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Messaging.Application.Abstractions;
using Marketplace.Modules.Messaging.Domain.Conversations;

namespace Marketplace.Modules.Messaging.Infrastructure.Persistence;

/// <summary>DbContext du module Messaging (schéma « messaging »).</summary>
public sealed class MessagingDbContext : ModuleDbContext, IMessagingUnitOfWork
{
    public const string SchemaName = "messaging";

    public MessagingDbContext(
        DbContextOptions<MessagingDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Conversation> Conversations => Set<Conversation>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MessagingDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
