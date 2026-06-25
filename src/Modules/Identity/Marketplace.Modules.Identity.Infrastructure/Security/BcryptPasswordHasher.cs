using Marketplace.Modules.Identity.Application.Abstractions;

namespace Marketplace.Modules.Identity.Infrastructure.Security;

/// <summary>Hash de mot de passe via BCrypt (work factor 12 par défaut).</summary>
internal sealed class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string password)
        => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string passwordHash, string password)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
