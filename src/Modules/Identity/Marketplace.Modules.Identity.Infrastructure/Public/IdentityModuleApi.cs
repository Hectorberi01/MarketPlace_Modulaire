using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Identity.Domain.PaymentMethods;
using Marketplace.Modules.Identity.Domain.Users;
using Marketplace.Modules.Identity.Infrastructure.Persistence;

namespace Marketplace.Modules.Identity.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Identity. Lecture seule,
/// projetée vers le DTO de Contracts. Les autres modules ne voient que ça.
/// </summary>
internal sealed class IdentityModuleApi : IIdentityModuleApi
{
    private readonly IdentityDbContext _dbContext;

    public IdentityModuleApi(IdentityDbContext dbContext)
        => _dbContext = dbContext;

    public async Task<UserSummary?> GetUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var id = new UserId(userId);
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(u => u.RoleAssignments)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

        return user is null ? null : Map(user);
    }

    public async Task<UserSummary?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var emailResult = Email.Create(email);
        if (emailResult.IsFailure)
        {
            return null;
        }

        var value = emailResult.Value;
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(u => u.RoleAssignments)
            .FirstOrDefaultAsync(u => u.Email == value, cancellationToken);

        return user is null ? null : Map(user);
    }

    public async Task<UserPayoutAccount?> GetDefaultPayoutAccountAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var method = await _dbContext.PaymentMethods
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.Type == PaymentMethodType.MobileMoney)
            // Le « par défaut » d'abord, puis le plus récent. L'ordre est explicite
            // plutôt que laissé au hasard de la base : un client qui a trois numéros
            // doit être payé sur celui qu'il a choisi, pas sur celui que Postgres rend
            // en premier ce jour-là.
            .OrderByDescending(p => p.IsDefault)
            .ThenByDescending(p => p.CreatedOnUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (method is null)
        {
            return null;
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == new UserId(userId), cancellationToken);

        var holder = string.IsNullOrWhiteSpace(method.HolderName)
            ? $"{user?.FirstName} {user?.LastName}".Trim()
            : method.HolderName!;

        return new UserPayoutAccount(
            method.Provider,
            method.AccountRef,
            string.IsNullOrWhiteSpace(holder) ? "Client" : holder);
    }

    /// <summary>
    /// ════════════════════════════════════════════════════════════════════════════
    /// CETTE PROJECTION S'ARRÊTAIT AUX RÔLES, ET LE RESTE PARTAIT À NULL EN SILENCE.
    ///
    /// `UserSummary` a gagné quatre champs optionnels au fil du temps — version des
    /// conditions acceptées, sa date, la vérification par un administrateur, et la photo
    /// de profil. Comme ils sont OPTIONNELS, ne pas les passer compile parfaitement :
    /// le compilateur remplit les valeurs par défaut, et personne n'est averti.
    ///
    /// C'est la projection que les BFF appellent réellement (`IIdentityModuleApi`), pas
    /// celle de `GetUserQueryHandler` qui, elle, les passait tous. Les deux construisaient
    /// donc le même type à partir du même agrégat, avec un résultat différent.
    ///
    /// CE QUE ÇA COÛTAIT : la photo de l'interlocuteur dans les listes de conversations
    /// valait toujours null. Les deux applications affichaient des initiales pour des
    /// comptes qui ont une photo, et rien — ni erreur, ni journal — ne le signalait.
    /// ════════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <remarks>
    /// LES PARAMÈTRES SONT NOMMÉS, et ce n'est pas une préférence de style : quatre
    /// valeurs optionnelles de suite dont deux `string?` et deux `DateTime?` se décalent
    /// sans que le compilateur bronche. C'est ainsi que ce défaut est né.
    /// </remarks>
    private static UserSummary Map(User user) => new(
        user.Id.Value,
        user.FirstName,
        user.LastName,
        user.Email.Value,
        user.PhoneNumber.Value,
        user.Status.ToString(),
        user.EmailVerified,
        user.MfaEnabled,
        user.RoleIds.ToList(),
        AcceptedTermsVersion: user.AcceptedTermsVersion,
        AcceptedTermsOnUtc: user.AcceptedTermsOnUtc,
        EmailVerifiedByAdminOnUtc: user.EmailVerifiedByAdminOnUtc,
        AvatarUrl: user.AvatarUrl);
}
