using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Identity.Domain.Roles;

namespace Marketplace.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Crée les rôles système par défaut s'ils n'existent pas (idempotent). Appelé au
/// démarrage après application des migrations. Le rôle « Buyer » est requis par
/// l'inscription (rôle assigné par défaut).
/// </summary>
public static class IdentityDataSeeder
{
    public static async Task SeedDefaultRolesAsync(IdentityDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var defaults = new (string Name, string Description, string[] Permissions)[]
        {
            ("Buyer", "Acheteur : parcours d'achat standard.", Array.Empty<string>()),
            ("Seller", "Vendeur : gestion de boutique et de produits.", new[] { "catalog.write", "offers.write", "orders.read" }),
            ("Admin", "Administrateur plateforme.", new[] { "users.manage", "roles.manage", "catalog.manage" }),
            ("Moderator", "Modérateur : validation contenus et avis.", new[] { "catalog.moderate", "reviews.moderate" })
        };

        foreach (var (name, description, permissions) in defaults)
        {
            var exists = await dbContext.Roles.AnyAsync(r => r.Name == name, cancellationToken);
            if (exists)
            {
                continue;
            }

            var roleResult = Role.Create(name, description, isSystem: true, permissions);
            if (roleResult.IsSuccess)
            {
                await dbContext.Roles.AddAsync(roleResult.Value, cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
