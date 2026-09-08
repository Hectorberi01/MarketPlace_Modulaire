using System.Security.Cryptography;
using System.Text;

namespace HbaExpress.Dispatch.Api.Security;

/// <summary>
/// Authentifie un appel venu de la MARKETPLACE.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// MÊME MÉCANISME QUE `PartnerServiceSignature` CÔTÉ MONOLITHE, DANS L'AUTRE SENS.
///
/// Là-bas, la marketplace vérifie que c'est bien ce service qui annonce une course
/// livrée. Ici, ce service vérifie que c'est bien la marketplace qui annonce une commande
/// prête. Deux directions, deux secrets DISTINCTS : partager une seule clé ferait qu'une
/// fuite d'un côté compromettrait les deux.
///
/// Recopié plutôt que référencé, pour la raison déjà donnée en persistance : ramener
/// Marketplace.BuildingBlocks.Infrastructure apporterait l'outbox et le découpage en
/// schémas du monolithe. Vingt lignes valent mieux que cette dépendance — et le jour où
/// ce service partira dans son propre dépôt, il n'y aura rien à démêler.
///
/// FERMÉ PAR DÉFAUT. Accepter un appel non signé parce qu'aucun secret n'est configuré
/// laisserait n'importe qui créer des courses, en annuler, ou déclarer un coursier
/// « actif ». En production, secret absent = appel rejeté.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class InboundSignature
{
    /// <summary>En-tête portant la signature HMAC-SHA256 du corps brut, en hexadécimal.</summary>
    public const string Header = "X-Marketplace-Signature";

    /// <summary>Emplacement du secret partagé avec la marketplace.</summary>
    public const string SecretKey = "Marketplace:InboundSecret";

    /// <summary>
    /// Autorise un appel NON signé lorsqu'aucun secret n'est configuré. Faux par défaut ;
    /// positionné à vrai par l'hôte en développement uniquement.
    /// </summary>
    public static bool AllowUnsignedWhenSecretMissing { get; set; }

    public static string Compute(string rawBody, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
    }

    /// <summary>
    /// L'appel est-il authentique ? Comparaison en TEMPS CONSTANT : une comparaison de
    /// chaînes ordinaire s'arrête au premier caractère différent, ce qui laisse deviner
    /// la signature octet par octet en mesurant le temps de réponse.
    /// </summary>
    public static bool Verify(string rawBody, string? signatureHeader, string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return AllowUnsignedWhenSecretMissing;
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        var expected = Compute(rawBody, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signatureHeader.Trim()));
    }
}
