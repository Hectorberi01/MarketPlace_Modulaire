using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Marketplace.BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Vérifie la signature d'un appel entrant venu d'un SERVICE PARTENAIRE — un système
/// qui n'est ni un utilisateur, ni un PSP.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI CE VÉRIFICATEUR EXISTE À CÔTÉ DE CELUI DES WEBHOOKS PSP.
///
/// La plateforme savait authentifier deux sortes d'appelants : un UTILISATEUR par son
/// jeton, et un PRESTATAIRE DE PAIEMENT par la signature de son webhook. Le service
/// coursier n'est ni l'un ni l'autre : il n'a pas de compte, et son secret n'a rien à
/// faire dans la configuration des passerelles de paiement.
///
/// Le mécanisme est le même — HMAC-SHA256, comparaison en temps constant, FERMÉ PAR
/// DÉFAUT — mais le secret est distinct, et c'est le point : un secret par
/// interlocuteur, pour qu'une fuite ne compromette qu'un seul canal.
///
/// FERMÉ PAR DÉFAUT, ET IL FAUT INSISTER. Le seul appel que cet endpoint autorise fait
/// SORTIR de l'argent du portefeuille de la plateforme vers celui d'un coursier.
/// Accepter un appel non signé parce qu'aucun secret n'est configuré reviendrait à
/// laisser n'importe qui se payer. En production, secret absent = appel rejeté.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// ═════════════════════════════════════════════════════════════════════════════
/// L'HORODATAGE EST DANS LA CHAÎNE SIGNÉE. SANS LUI, UNE SIGNATURE NE PÉRIME JAMAIS.
///
/// La signature ne portait que sur le corps. Un corps signé capturé UNE FOIS — dans un
/// journal, chez un proxy, dans un dépôt de logs — restait valable indéfiniment, et
/// rejouable autant de fois qu'on veut. Ce n'est pas une faiblesse du HMAC : c'est
/// qu'un message sans horloge ne dit pas QUAND il a été émis, donc rien ne permet de
/// le déclarer périmé.
///
/// La chaîne signée est désormais « horodatage.corps ». Elle lie les deux : changer
/// l'horodatage pour rafraîchir un vieux message invalide la signature, et
/// réutiliser la signature impose de réutiliser l'horodatage, qui sera hors fenêtre.
///
/// LE POINT ET LE FORMAT COMPTENT. Sans séparateur, un horodatage de longueur variable
/// collé au corps produirait deux messages différents avec la même chaîne signée. Les
/// deux côtés doivent écrire l'horodatage IDENTIQUEMENT — d'où
/// <see cref="FormatTimestamp"/>, seul endroit où ce format est décidé.
///
/// CE QUE ÇA NE FAIT PAS : rejouer un message dans les cinq minutes reste possible.
/// Fermer cette fenêtre demanderait un magasin de nonces partagé entre instances —
/// donc un état, donc une panne de plus. Les routes concernées sont soit idempotentes,
/// soit protégées en aval (voir Courier.ApplyStanding, qui refuse une annonce plus
/// ancienne que la dernière appliquée). Le jour où une route non idempotente et non
/// protégée apparaît, la fenêtre ne suffit plus.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class PartnerServiceSignature
{
    /// <summary>En-tête portant l'horodatage d'émission, en secondes Unix.</summary>
    public const string TimestampHeader = "X-Signature-Timestamp";

    /// <summary>
    /// Écart maximal toléré entre l'horodatage annoncé et l'heure locale, dans les DEUX
    /// sens : une horloge d'émetteur en avance est aussi suspecte qu'un message vieux.
    ///
    /// Cinq minutes couvrent une dérive d'horloge ordinaire entre deux conteneurs et la
    /// latence d'un drain d'outbox qui réessaie. Descendre plus bas ferait échouer des
    /// appels légitimes sur des machines dont personne ne surveille NTP.
    /// </summary>
    public static TimeSpan MaxClockSkew { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Autorise un appel NON signé lorsqu'aucun secret n'est configuré. Faux par défaut ;
    /// à positionner à vrai uniquement en environnement de développement.
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
    /// ne puisse pas poser l'une en oubliant l'autre — l'appel serait rejeté à l'arrivée,
    /// et l'erreur ne se verrait qu'en production.
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

        // Le RECALCUL de la fenêtre se fait sur la valeur PARSÉE, mais la SIGNATURE se
        // vérifie sur la chaîne REÇUE. Signer la valeur reformatée accepterait « 007 »
        // et « 7 » comme un même message, donc deux en-têtes distincts pour une seule
        // signature — de quoi contourner un futur magasin de nonces.
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
