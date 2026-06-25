using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.Roles;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// DbContext du module Identity. Vit dans le schéma « identity » : pas de JOIN ni
/// de foreign key vers un autre schéma. Hérite de ModuleDbContext pour l'Unit of
/// Work (dispatch des domain events) et l'outbox.
/// </summary>
public sealed class IdentityDbContext : ModuleDbContext, IIdentityUnitOfWork
{
    public const string SchemaName = "identity";

    public IdentityDbContext(
        DbContextOptions<IdentityDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
