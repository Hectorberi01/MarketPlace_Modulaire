using HBA.Users.Application.Abstractions;
using HBA.Users.Domain.Addresses;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HBA.Users.Infrastructure.Persistence;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LE MODULE USER — « QUI EST LA PERSONNE ? »
///
/// Le cahier d'architecture sépare deux questions que le code réunissait :
///
///   • IDENTITY répond à « qui peut se connecter ? » — mot de passe, jetons,
///     rôles, vérifications. C'est de la sécurité d'accès.
///   • USER répond à « qui est la personne ? » — profil, avatar, adresses,
///     préférences. C'est du métier.
///
/// La distinction n'est pas cosmétique. Le carnet d'adresses n'a aucune raison
/// d'être verrouillé derrière les mêmes contraintes qu'un magasin de mots de
/// passe, et un magasin de mots de passe n'a aucune raison de grossir à chaque
/// fois qu'un acheteur ajoute une adresse de livraison.
///
/// ⚠️ LA RÉFÉRENCE VA DANS UN SEUL SENS.
///
/// User connaît le <c>UserId</c> émis par Identity ; Identity ne connaît pas User.
/// C'est ce qui permet de supprimer un profil sans toucher au compte, et de
/// vérifier un mot de passe sans charger un carnet d'adresses.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class UsersDbContext : ModuleDbContext, IUsersUnitOfWork
{
    public const string SchemaName = "users";

    public UsersDbContext(
        DbContextOptions<UsersDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<Address> Addresses => Set<Address>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UsersDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
