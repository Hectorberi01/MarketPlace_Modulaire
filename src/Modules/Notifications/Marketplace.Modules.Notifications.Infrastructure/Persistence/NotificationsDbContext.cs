using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Notifications.Application.Abstractions;
using Marketplace.Modules.Notifications.Domain.Notifications;

namespace Marketplace.Modules.Notifications.Infrastructure.Persistence;

/// <summary>DbContext du module Notifications (schéma « notifications »).</summary>
public sealed class NotificationsDbContext : ModuleDbContext, INotificationsUnitOfWork
{
    public const string SchemaName = "notifications";

    public NotificationsDbContext(
        DbContextOptions<NotificationsDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Notification> Notifications => Set<Notification>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
