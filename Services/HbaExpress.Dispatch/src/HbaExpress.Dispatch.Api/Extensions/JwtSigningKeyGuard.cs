namespace HbaExpress.Dispatch.Api.Extensions;

/// <summary>
/// Refuse de démarrer en PRODUCTION avec la clé de signature JWT publique de
/// développement.
///
/// Même garde-fou que <c>Marketplace.Api.Extensions.JwtKeyGuard</c>, et pour la même
/// raison : cette clé est COMMITÉE dans le dépôt, donc connue de quiconque a lu le code.
/// Avec elle, n'importe qui forge un jeton portant l'identifiant d'un coursier — et
/// accepte ses courses, ou les marque livrées à sa place.
///
/// Recopié plutôt que référencé : ce service ne dépend pas de l'hôte du monolithe, et
/// n'a aucune raison de commencer pour trente lignes. La valeur, elle, doit rester
/// identique — c'est la même clé des deux côtés.
/// </summary>
public static class JwtSigningKeyGuard
{
    /// <summary>
    /// La clé de développement, committée et donc PUBLIQUE. Elle ne protège rien, et
    /// c'est assumé : son seul rôle est de permettre de lancer le projet après un clone.
    /// </summary>
    public const string DevelopmentKey = "DEV-ONLY-PUBLIC-KEY-NOT-A-SECRET-DO-NOT-USE-IN-PRODUCTION";

    /// <summary>Longueur minimale pour HS256 (256 bits = 32 octets).</summary>
    private const int MinimumKeyLength = 32;

    public static void Ensure(string signingKey, IHostEnvironment environment)
    {
        if (!environment.IsProduction())
        {
            return;
        }

        if (string.Equals(signingKey, DevelopmentKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "CLÉ DE SIGNATURE JWT DE DÉVELOPPEMENT EN PRODUCTION.\n\n" +
                "La clé utilisée est celle qui est COMMITÉE DANS LE DÉPÔT — donc publique.\n\n" +
                "Avec elle, n'importe qui peut forger un jeton portant l'identifiant d'un coursier, " +
                "accepter ses courses et les marquer livrées. Le démarrage est refusé délibérément.\n\n" +
                "Définissez JWT_SIGNING_KEY avec la MÊME valeur que la marketplace :\n" +
                "    openssl rand -base64 48");
        }

        if (signingKey.Length < MinimumKeyLength)
        {
            throw new InvalidOperationException(
                $"CLÉ DE SIGNATURE JWT TROP COURTE ({signingKey.Length} caractères). " +
                $"HS256 exige au moins {MinimumKeyLength} octets.");
        }
    }
}
