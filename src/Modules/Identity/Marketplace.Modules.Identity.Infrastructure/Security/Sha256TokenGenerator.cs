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

    public (string Code, string Hash) GenerateNumericCode(int digits = 6)
    {
        // Chiffres cryptographiquement aléatoires, sans biais de modulo
        // (RandomNumberGenerator.GetInt32 est uniforme sur [0, 10)).
        var chars = new char[digits];
        for (var i = 0; i < digits; i++)
        {
            chars[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        }
        var code = new string(chars);
        return (code, Hash(code));
    }

    public string Hash(string rawToken)
    {
        var hashed = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hashed);
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
