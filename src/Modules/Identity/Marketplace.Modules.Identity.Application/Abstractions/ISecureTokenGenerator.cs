namespace Marketplace.Modules.Identity.Application.Abstractions;

/// <summary>
/// Génère des jetons opaques aléatoires (refresh tokens, liens de vérification)
/// et calcule leur hash. On ne stocke que le hash ; le jeton en clair n'est
/// transmis qu'une fois au client.
/// </summary>
public interface ISecureTokenGenerator
{
    /// <summary>Crée un jeton : renvoie la valeur en clair et son hash.</summary>
    (string Raw, string Hash) Generate();

    /// <summary>Calcule le hash d'un jeton fourni (pour comparaison).</summary>
    string Hash(string rawToken);
}
