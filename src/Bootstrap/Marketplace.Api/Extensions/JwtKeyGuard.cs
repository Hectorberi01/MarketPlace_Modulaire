using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Marketplace.Api.Extensions;

/// <summary>
/// Refuse de démarrer en PRODUCTION avec la clé de signature JWT publique de
/// développement.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CE GARDE-FOU EXISTE
///
/// Une clé de signature JWT était COMMITÉE dans les cinq `appsettings.json`. Elle est
/// donc dans l'historique Git — pour toujours. Un `git rm` n'y change rien : on ne
/// retire pas un secret d'un historique, on le remplace.
///
/// Elle a été remplacée par une clé de développement EXPLICITEMENT PUBLIQUE, qui rend
/// le dépôt utilisable après un simple clone (personne ne veut configurer un vault pour
/// lancer les tests). Mais une clé publique connue de tous permet de FORGER n'importe
/// quel jeton : un environnement qui l'utiliserait par mégarde accepterait des jetons
/// « Admin » fabriqués par le premier venu.
///
/// Le compose de production impose déjà l'override (`${JWT_SIGNING_KEY:?…}` échoue si la
/// variable manque). Ce garde-fou est la CEINTURE : il couvre tous les autres chemins de
/// déploiement — un systemd mal configuré, un environnement de recette monté à la main,
/// un futur pipeline qui oublierait la variable.
///
/// Deux barrières valent mieux qu'une quand la conséquence de l'oubli est
/// « n'importe qui devient administrateur ».
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class JwtKeyGuard
{
    /// <summary>
    /// La clé de développement, committée et donc PUBLIQUE. Elle ne protège rien, et
    /// c'est assumé : son seul rôle est de permettre à un développeur de lancer le
    /// projet immédiatement après un clone.
    /// </summary>
    public const string DevelopmentKey = "DEV-ONLY-PUBLIC-KEY-NOT-A-SECRET-DO-NOT-USE-IN-PRODUCTION";

    /// <summary>Longueur minimale pour HS256 (256 bits = 32 octets).</summary>
    private const int MinimumKeyLength = 32;

    /// <summary>
    /// À appeler juste après la lecture de <c>Jwt:SigningKey</c>, avant
    /// <c>AddAuthentication</c>.
    /// </summary>
    public static void EnsureProductionKey(string signingKey, IHostEnvironment environment)
    {
        if (!environment.IsProduction())
        {
            return;
        }

        if (string.Equals(signingKey, DevelopmentKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "CLÉ DE SIGNATURE JWT DE DÉVELOPPEMENT EN PRODUCTION.\n\n" +
                "La clé utilisée est celle qui est COMMITÉE DANS LE DÉPÔT — donc publique, et connue de " +
                "quiconque a lu le code.\n\n" +
                "Avec elle, n'importe qui peut FORGER un jeton de rôle « Admin » et prendre le contrôle " +
                "de la plateforme. Le démarrage est refusé délibérément.\n\n" +
                "Définissez JWT_SIGNING_KEY (variable d'environnement / vault) avec une valeur aléatoire " +
                "d'au moins 32 octets :\n" +
                "    openssl rand -base64 48");
        }

        if (signingKey.Length < MinimumKeyLength)
        {
            throw new InvalidOperationException(
                $"CLÉ DE SIGNATURE JWT TROP COURTE ({signingKey.Length} caractères).\n\n" +
                $"HS256 exige au moins {MinimumKeyLength} octets. Une clé plus courte est cassable par " +
                "force brute — et le fait qu'elle « fonctionne » ne prouve rien.\n\n" +
                "    openssl rand -base64 48");
        }
    }
}
