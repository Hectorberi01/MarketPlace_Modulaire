using System.Globalization;
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
/// schémas du monolithe. Ces lignes valent mieux que cette dépendance — et le jour où
/// ce service partira dans son propre dépôt, il n'y aura rien à démêler.
///
/// LA CONTREPARTIE DE LA RECOPIE : les deux fichiers doivent rester IDENTIQUES dans leur
/// calcul. Chaîne signée « horodatage.corps », horodatage en secondes Unix, fenêtre de
/// cinq minutes. Modifier l'un sans l'autre coupe le canal, dans un seul sens, et le
/// symptôme est un 401 en production sur une configuration qui paraît juste.
///
/// FERMÉ PAR DÉFAUT. Accepter un appel non signé parce qu'aucun secret n'est configuré
/// laisserait n'importe qui créer des courses, en annuler, ou déclarer un coursier
/// « actif ». En production, secret absent = appel rejeté.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// ═════════════════════════════════════════════════════════════════════════════
/// L'HORODATAGE EST DANS LA CHAÎNE SIGNÉE, ET C'EST CE SERVICE QUI EN AVAIT LE PLUS
/// BESOIN.
///
/// Une signature qui ne porte que le corps ne périme jamais. Deux des trois routes
/// s'en accommodaient — `orders/ready-for-delivery` est idempotente sur l'identifiant
/// de commande, `orders/cancelled` est bénigne. La troisième mordait :
///
///   REJOUER UN ANCIEN MESSAGE « standing = Active » RÉACTIVAIT UN COURSIER SUSPENDU.
///
/// `ApplyStandingAsync` recopie le statut annoncé — c'est précisément ce qu'on lui
/// demande de faire, et c'est pour ça que la défense ne pouvait pas être là.
///
/// La fenêtre de cinq minutes ramène « pour toujours » à « pendant cinq minutes ». Ce
/// qui ferme le reste est ailleurs, en profondeur : Courier.ApplyStanding refuse une
/// annonce plus ancienne que la dernière appliquée. Les deux sont nécessaires — la
/// fenêtre parce qu'elle protège les trois routes, le verrou monotone parce qu'il
/// protège celle qui mord, y compris contre un simple désordre de livraison de l'outbox.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class InboundSignature
{
    /// <summary>En-tête portant la signature HMAC-SHA256, en hexadécimal.</summary>
    public const string Header = "X-Marketplace-Signature";

    /// <summary>En-tête portant l'horodatage d'émission, en secondes Unix.</summary>
    public const string TimestampHeader = "X-Signature-Timestamp";

    /// <summary>Emplacement du secret partagé avec la marketplace.</summary>
    public const string SecretKey = "Marketplace:InboundSecret";

    /// <summary>
    /// Écart maximal toléré entre l'horodatage annoncé et l'heure locale, dans les DEUX
    /// sens : une horloge d'émetteur en avance est aussi suspecte qu'un message vieux.
    /// </summary>
    public static TimeSpan MaxClockSkew { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Autorise un appel NON signé lorsqu'aucun secret n'est configuré. Faux par défaut ;
    /// positionné à vrai par l'hôte en développement uniquement.
    /// </summary>
    public static bool AllowUnsignedWhenSecretMissing { get; set; }

    /// <summary>L'horodatage tel qu'il voyage, et tel qu'il est signé. Un seul format.</summary>
    public static string FormatTimestamp(DateTimeOffset instant)
        => instant.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>Signature HMAC-SHA256 de « horodatage.corps », en hexadécimal minuscule.</summary>
    public static string Compute(string timestamp, string rawBody, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signed = string.Concat(timestamp, ".", rawBody);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signed))).ToLowerInvariant();
    }

    /// <summary>
    /// Horodate et signe un corps sortant. Renvoie les deux valeurs ENSEMBLE, pour qu'on
    /// ne puisse pas poser l'une en oubliant l'autre.
    /// </summary>
    public static (string Timestamp, string Signature) Sign(
        string rawBody, string secret, DateTimeOffset? now = null)
    {
        var timestamp = FormatTimestamp(now ?? DateTimeOffset.UtcNow);
        return (timestamp, Compute(timestamp, rawBody, secret));
    }

    /// <summary>
    /// L'appel est-il authentique ET frais ? Comparaison en TEMPS CONSTANT : une
    /// comparaison de chaînes ordinaire s'arrête au premier caractère différent, ce qui
    /// laisse deviner la signature octet par octet en mesurant le temps de réponse.
    /// </summary>
    public static bool Verify(
        string rawBody,
        string? timestampHeader,
        string? signatureHeader,
        string? secret,
        DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return AllowUnsignedWhenSecretMissing;
        }

        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrWhiteSpace(timestampHeader))
        {
            return false;
        }

        var timestamp = timestampHeader.Trim();

        if (!long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        // La FENÊTRE se calcule sur la valeur parsée, la SIGNATURE se vérifie sur la
        // chaîne REÇUE : signer la valeur reformatée ferait de « 007 » et « 7 » deux
        // en-têtes distincts pour une seule signature.
        var age = (now ?? DateTimeOffset.UtcNow) - DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (age.Duration() > MaxClockSkew)
        {
            return false;
        }

        var expected = Compute(timestamp, rawBody, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signatureHeader.Trim()));
    }
}
