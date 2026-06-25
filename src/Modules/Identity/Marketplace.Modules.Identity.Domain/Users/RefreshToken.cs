using Marketplace.BuildingBlocks.Domain.Primitives;

namespace Marketplace.Modules.Identity.Domain.Users;

/// <summary>
/// Jeton de rafraîchissement. On ne stocke jamais le token en clair : seul son
/// hash est persisté. Entité enfant de l'agrégat User.
/// </summary>
public sealed class RefreshToken : Entity<Guid>
{
    private RefreshToken()
    {
    }

    internal RefreshToken(Guid id, string tokenHash, DateTime expiresOnUtc)
        : base(id)
    {
        TokenHash = tokenHash;
        ExpiresOnUtc = expiresOnUtc;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public string TokenHash { get; private set; } = default!;
    public DateTime ExpiresOnUtc { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? RevokedOnUtc { get; private set; }

    public bool IsActive(DateTime nowUtc) => RevokedOnUtc is null && ExpiresOnUtc > nowUtc;

    internal void Revoke() => RevokedOnUtc = DateTime.UtcNow;
}
