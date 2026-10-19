using System.Security.Cryptography;
using System.Text;
using Marketplace.Modules.Platform.Application.Abstractions;

namespace Marketplace.Modules.Platform.Infrastructure.Security;

/// <summary>
/// Chiffrement AES-256-GCM des secrets des fournisseurs.
///
/// GCM ET NON CBC : GCM est AUTHENTIFIÉ. Une valeur modifiée en base — par un accès
/// direct au SQL, par une restauration partielle — fait échouer le déchiffrement au
/// lieu de rendre des octets faux qui partiraient tels quels chez un PSP.
///
/// UN NONCE PAR VALEUR, tiré aléatoirement. Réutiliser un nonce avec la même clé
/// casse GCM, pas seulement la confidentialité : on peut en déduire de quoi forger
/// des messages authentifiés. D'où un tirage à chaque chiffrement, stocké à côté.
///
/// Format : « v1:nonce:chiffré:tag », chaque segment en base64. Le préfixe de version
/// existe pour qu'une rotation d'algorithme puisse LIRE l'ancien format : sans lui, le
/// jour où l'on change de schéma, tous les secrets existants deviennent illisibles.
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const string Version = "v1";
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private readonly byte[] _key;

    public AesGcmSecretProtector(byte[] key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("La clé maître doit faire exactement 32 octets (AES-256).", nameof(key));
        }

        _key = key;
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var clair = Encoding.UTF8.GetBytes(plaintext);
        var chiffre = new byte[clair.Length];
        var tag = new byte[TagBytes];

        // `tagSizeInBytes` est EXIGÉ : le constructeur sans taille est obsolète en
        // .NET 8 (SYSLIB0053), et le dépôt compile en « avertissements = erreurs ».
        using var aes = new AesGcm(_key, TagBytes);
        aes.Encrypt(nonce, clair, chiffre, tag);

        return string.Join(':', Version,
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(chiffre),
            Convert.ToBase64String(tag));
    }

    public string Unprotect(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return string.Empty;
        }

        var segments = protectedValue.Split(':');
        if (segments.Length != 4 || segments[0] != Version)
        {
            throw new CryptographicException(
                "Secret illisible : format inattendu. La valeur a été écrite par une autre version.");
        }

        var nonce = Convert.FromBase64String(segments[1]);
        var chiffre = Convert.FromBase64String(segments[2]);
        var tag = Convert.FromBase64String(segments[3]);
        var clair = new byte[chiffre.Length];

        using var aes = new AesGcm(_key, TagBytes);

        // On NE RATTRAPE PAS l'échec d'authentification. Il ne dit pas « valeur
        // bizarre » mais « la base a changé sous nos pieds, ou ce n'est pas la bonne
        // clé maître ». Renvoyer une chaîne vide ferait appeler le PSP sans clé, et
        // l'erreur remonterait de chez lui, méconnaissable.
        aes.Decrypt(nonce, chiffre, tag, clair);

        return Encoding.UTF8.GetString(clair);
    }

    public string Preview(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return string.Empty;
        }

        try
        {
            var clair = Unprotect(protectedValue);
            // QUATRE CARACTÈRES, pas plus : de quoi reconnaître « re_… » ou « sk_live… »
            // et vérifier qu'on a posé la bonne clé, jamais de quoi s'en servir.
            return clair.Length <= 4 ? "…" : string.Concat(clair.AsSpan(0, 4), "…");
        }
        catch (CryptographicException)
        {
            return "illisible";
        }
    }
}
