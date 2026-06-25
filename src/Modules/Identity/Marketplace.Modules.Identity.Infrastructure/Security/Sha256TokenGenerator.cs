using System.Security.Cryptography;
using System.Text;
using Marketplace.Modules.Identity.Application.Abstractions;

namespace Marketplace.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Génère des jetons opaques (256 bits, base64url) et calcule leur hash SHA-256.
/// On ne stocke que le hash ; le jeton en clair n'est transmis qu'une fois.
/// </summary>
internal sealed class Sha256TokenGenerator : ISecureTokenGenerator
{
    public (string Raw, string Hash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var raw = Base64Url(bytes);
        return (raw, Hash(raw));
    }

    public string Hash(string rawToken)
    {
        var hashed = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hashed);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
