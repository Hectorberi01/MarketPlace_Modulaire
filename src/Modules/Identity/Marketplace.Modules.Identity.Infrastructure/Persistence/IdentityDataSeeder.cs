using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.Roles;
using Marketplace.Modules.Identity.Domain.Users;

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

    /// <summary>
    /// Crée un compte administrateur (rôle Admin, e-mail vérifié, actif) s'il
    /// n'existe pas déjà. Idempotent. Réservé au bootstrap/dev : le mot de passe
    /// est haché par le service d'infrastructure (BCrypt).
    /// </summary>
    public static async Task SeedAdminUserAsync(
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        string email,
        string password,
        string firstName,
        string lastName,
        string phoneNumber,
        CancellationToken cancellationToken = default)
    {
        var emailResult = Email.Create(email);
        var phoneResult = PhoneNumber.Create(phoneNumber);
        if (emailResult.IsFailure || phoneResult.IsFailure || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        // Idempotence : on ne recrée pas l'admin s'il existe déjà.
        var alreadyExists = await dbContext.Users.AnyAsync(u => u.Email == emailResult.Value, cancellationToken);
        if (alreadyExists)
        {
            return;
        }

        var adminRole = await dbContext.Roles.FirstOrDefaultAsync(r => r.Name == "Admin", cancellationToken);
        if (adminRole is null)
        {
            return;
        }

        var passwordHash = passwordHasher.Hash(password);
        // Token de vérification connu : réutilisé pour confirmer l'e-mail tout de suite.
        const string verificationTokenHash = "seed-admin-email-verification";

        var userResult = User.Register(
            firstName, lastName, emailResult.Value, phoneResult.Value,
            passwordHash, verificationTokenHash, DateTime.UtcNow.AddYears(1));
        if (userResult.IsFailure)
        {
            return;
        }

        var user = userResult.Value;
        user.ConfirmEmail(verificationTokenHash, DateTime.UtcNow); // e-mail vérifié + compte actif
        user.AssignRole(adminRole.Id.Value);

        await dbContext.Users.AddAsync(user, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Crée un compte vendeur (rôle Seller, e-mail vérifié, actif) s'il n'existe
    /// pas déjà, et renvoie l'identifiant de l'utilisateur (existant ou créé) afin
    /// de pouvoir rattacher un profil boutique. Idempotent. Réservé au bootstrap/dev.
    /// </summary>
    public static async Task<Guid?> SeedSellerUserAsync(
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        string email,
        string password,
        string firstName,
        string lastName,
        string phoneNumber,
        CancellationToken cancellationToken = default)
    {
        var emailResult = Email.Create(email);
        var phoneResult = PhoneNumber.Create(phoneNumber);
        if (emailResult.IsFailure || phoneResult.IsFailure || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var existing = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == emailResult.Value, cancellationToken);
        if (existing is not null)
        {
            return existing.Id.Value; // déjà présent : on renvoie son id pour le rattachement boutique.
        }

        var sellerRole = await dbContext.Roles.FirstOrDefaultAsync(r => r.Name == "Seller", cancellationToken);
        if (sellerRole is null)
        {
            return null;
        }

        var passwordHash = passwordHasher.Hash(password);
        const string verificationTokenHash = "seed-seller-email-verification";

        var userResult = User.Register(
            firstName, lastName, emailResult.Value, phoneResult.Value,
            passwordHash, verificationTokenHash, DateTime.UtcNow.AddYears(1));
        if (userResult.IsFailure)
        {
            return null;
        }

        var user = userResult.Value;
        user.ConfirmEmail(verificationTokenHash, DateTime.UtcNow); // e-mail vérifié + compte actif
        user.AssignRole(sellerRole.Id.Value);

        await dbContext.Users.AddAsync(user, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return user.Id.Value;
    }
}
