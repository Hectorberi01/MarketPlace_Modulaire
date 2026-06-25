namespace Marketplace.Modules.Identity.Application.Abstractions;

/// <summary>Hash et vérification de mot de passe (implémenté avec BCrypt en Infrastructure).</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string passwordHash, string password);
}
