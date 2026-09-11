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

        // ═════════════════════════════════════════════════════════════════════════
        // LES RÔLES SYSTÈME SONT DÉFINIS PAR LE CODE : L'AMORÇAGE LES RÉALIGNE.
        //
        // Cette boucle se contentait de SAUTER un rôle déjà présent. Tant que la console
        // pouvait modifier les permissions, une base existante restait rattrapable à la
        // main ; depuis que le domaine refuse cette modification — pour empêcher qu'on
        // décoche les droits d'« Admin » sans pouvoir se les rendre — plus AUCUN chemin
        // ne permettait d'ajouter une permission déclarée ici. Le garde aurait muré la
        // dernière issue.
        //
        // Le réalignement ne vaut que pour les rôles SYSTÈME, dont la source de vérité
        // est le dépôt. Un rôle créé à la main n'est jamais touché : `defaults` ne le
        // nomme pas, et `SyncSystemPermissions` le refuserait de toute façon.
        //
        // CONSÉQUENCE À CONNAÎTRE : retirer une permission de `defaults` la retire aussi
        // en base au prochain démarrage. C'est bien ce qu'on veut d'une déclaration —
        // mais c'est un déploiement qui décide, plus un administrateur.
        // ═════════════════════════════════════════════════════════════════════════
        foreach (var (name, description, permissions) in defaults)
        {
            var existing = await dbContext.Roles.FirstOrDefaultAsync(r => r.Name == name, cancellationToken);
            if (existing is not null)
            {
                // Le résultat est ignoré à dessein : un rôle porté par `defaults` est
                // système par construction, et le format des permissions est écrit
                // juste au-dessus. Un échec ici signalerait une faute de frappe dans ce
                // fichier, que le démarrage n'a pas à transformer en panne.
                existing.SyncSystemPermissions(permissions);
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
    /// Issue de l'amorçage du compte administrateur.
    ///
    /// ⚠️ Ce type existe parce que la méthode renvoyait auparavant `void` et sortait
    /// silencieusement dans QUATRE cas d'échec. L'appelant journalisait « compte
    /// vérifié/créé » sans condition — un message rassurant et faux. Sur une base
    /// neuve, l'échec ne se manifestait qu'au moment où personne ne parvenait à se
    /// connecter, sans la moindre trace pour l'expliquer.
    /// </summary>
    public enum AdminSeedOutcome
    {
        /// <summary>Compte créé, actif, doté du rôle Admin.</summary>
        Created,

        /// <summary>Déjà présent : rien n'a été modifié (le mot de passe n'est jamais réinitialisé).</summary>
        AlreadyPresent,

        /// <summary>Adresse e-mail rejetée par le domaine.</summary>
        InvalidEmail,

        /// <summary>Numéro de téléphone rejeté par le domaine (8 à 15 chiffres attendus).</summary>
        InvalidPhone,

        /// <summary>Mot de passe absent ou vide.</summary>
        EmptyPassword,

        /// <summary>Rôle « Admin » introuvable — l'amorçage des rôles n'a pas eu lieu.</summary>
        AdminRoleMissing,

        /// <summary>Le domaine a refusé l'inscription (prénom, nom ou hachage manquant).</summary>
        RegistrationRejected,
    }

    /// <summary>
    /// Crée un compte administrateur ACTIF doté du rôle Admin s'il n'existe pas
    /// déjà. Idempotent : un redéploiement ne réinitialise jamais le mot de passe.
    ///
    /// Renvoie l'issue de l'opération — voir <see cref="AdminSeedOutcome"/> — afin
    /// que l'appelant puisse en rendre compte fidèlement. C'est le seul compte par
    /// lequel la plateforme peut être ouverte : son échec doit être bruyant.
    /// </summary>
    public static async Task<AdminSeedOutcome> SeedAdminUserAsync(
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        string email,
        string password,
        string firstName,
        string lastName,
        string phoneNumber,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return AdminSeedOutcome.EmptyPassword;
        }

        var emailResult = Email.Create(email);
        if (emailResult.IsFailure)
        {
            return AdminSeedOutcome.InvalidEmail;
        }

        var phoneResult = PhoneNumber.Create(phoneNumber);
        if (phoneResult.IsFailure)
        {
            return AdminSeedOutcome.InvalidPhone;
        }

        // Idempotence : on ne recrée pas l'admin s'il existe déjà.
        var alreadyExists = await dbContext.Users.AnyAsync(u => u.Email == emailResult.Value, cancellationToken);
        if (alreadyExists)
        {
            return AdminSeedOutcome.AlreadyPresent;
        }

        var adminRole = await dbContext.Roles.FirstOrDefaultAsync(r => r.Name == "Admin", cancellationToken);
        if (adminRole is null)
        {
            return AdminSeedOutcome.AdminRoleMissing;
        }

        var passwordHash = passwordHasher.Hash(password);
        // Token de vérification connu : réutilisé pour confirmer l'e-mail tout de suite.
        const string verificationTokenHash = "seed-admin-email-verification";

        var userResult = User.Register(
            firstName, lastName, emailResult.Value, phoneResult.Value,
            passwordHash, verificationTokenHash, DateTime.UtcNow.AddYears(1));
        if (userResult.IsFailure)
        {
            return AdminSeedOutcome.RegistrationRejected;
        }

        var user = userResult.Value;
        // `Approve()`, et non `ConfirmEmail()`.
        //
        // Le compte d'amorçage doit être ACTIF — sans lui, personne ne peut se
        // connecter pour valider les autres, et la plateforme démarre verrouillée.
        // Mais il ne doit pas prétendre que son e-mail a été vérifié : aucun message
        // n'a été envoyé, personne n'a cliqué sur rien. `Approve()` active le compte
        // sans inventer ce fait.
        //
        // C'est bien `Status` — et non `EmailVerified` — que contrôle la connexion
        // (voir LoginCommandHandler) : ce compte peut donc se connecter immédiatement.
        user.Approve();
        user.AssignRole(adminRole.Id.Value);

        await dbContext.Users.AddAsync(user, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return AdminSeedOutcome.Created;
    }

    /// <summary>
    /// Vérifie qu'un compte administrateur est réellement en état de se connecter :
    /// présent, ACTIF, et porteur du rôle Admin.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// Pourquoi relire la base plutôt que se fier à l'issue de l'amorçage.
    ///
    /// Trois conditions distinctes commandent la première connexion, et aucune n'est
    /// garantie par la seule création du compte :
    ///
    ///   • le STATUT doit être `Active` — c'est lui, et non `EmailVerified`, que
    ///     contrôle `LoginCommandHandler` ;
    ///   • le rôle `Admin` doit être assigné — sans lui, la console d'administration
    ///     répond « ce compte n'a pas accès à cette application » ;
    ///   • le compte doit exister, y compris lorsqu'il vient d'une base restaurée ou
    ///     d'un déploiement antérieur, cas où l'amorçage ne fait rien du tout.
    ///
    /// Un compte suspendu par un administrateur, ou dont le rôle a été retiré, passe
    /// l'amorçage sans erreur (`AlreadyPresent`) tout en étant incapable d'entrer.
    /// Seule une relecture le détecte.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    /// <returns>
    /// `null` si tout est en ordre ; sinon la raison, prête à être journalisée.
    /// </returns>
    public static async Task<string?> VerifyAdminCanSignInAsync(
        IdentityDbContext dbContext,
        string email,
        CancellationToken cancellationToken = default)
    {
        var emailResult = Email.Create(email);
        if (emailResult.IsFailure)
        {
            return "l'adresse configurée n'est pas une adresse e-mail valide";
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .Include(u => u.RoleAssignments)
            .FirstOrDefaultAsync(u => u.Email == emailResult.Value, cancellationToken);

        if (user is null)
        {
            return "aucun compte ne porte cette adresse";
        }

        if (user.Status != UserStatus.Active)
        {
            return $"le compte existe mais son statut est « {user.Status} » (attendu : Active)";
        }

        var adminRole = await dbContext.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Name == "Admin", cancellationToken);

        if (adminRole is null)
        {
            return "le rôle « Admin » n'existe pas en base";
        }

        if (!user.RoleAssignments.Any(a => a.RoleId == adminRole.Id.Value))
        {
            return "le compte existe et est actif, mais ne porte pas le rôle « Admin »";
        }

        return null;
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
        // `Approve()`, et non `ConfirmEmail()`.
        //
        // Le compte d'amorçage doit être ACTIF — sans lui, personne ne peut se
        // connecter pour valider les autres, et la plateforme démarre verrouillée.
        // Mais il ne doit pas prétendre que son e-mail a été vérifié : aucun message
        // n'a été envoyé, personne n'a cliqué sur rien. `Approve()` active le compte
        // sans inventer ce fait.
        user.Approve();
        user.AssignRole(sellerRole.Id.Value);

        await dbContext.Users.AddAsync(user, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return user.Id.Value;
    }
}
