using System.Security.Cryptography;
using System.Text;

namespace HBA.Deliveries.Domain.Webhooks;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LA SIGNATURE DES WEBHOOKS.
///
/// Sans elle, l'URL de rappel d'un partenaire est un endpoint public que
/// n'importe qui peut appeler. Un concurrent qui connaît l'adresse — elle finit
/// toujours par circuler — annonce « commande livrée » et le marchand clôt une
/// vente qui n'a jamais été livrée.
///
/// L'HORODATAGE EST DANS LA CHAÎNE SIGNÉE, ET C'EST TOUT L'INTÉRÊT.
///
/// Signer le seul corps produirait une signature valable ÉTERNELLEMENT : un appel
/// intercepté une fois pourrait être rejoué des mois plus tard, toujours
/// parfaitement signé. En signant « horodatage.corps », le partenaire peut
/// refuser ce qui est trop ancien — et un rejeu devient inopérant sans que nous
/// ayons à révoquer quoi que ce soit.
///
/// LE FORMAT EST CELUI DES PRESTATAIRES DE PAIEMENT
///
///     X-HBA-Signature: t=1723372800,v1=&lt;hex&gt;
///
/// Ce n'est pas de l'imitation : c'est le format que les bibliothèques de
/// vérification des intégrateurs savent déjà lire, et le préfixe « v1 » laisse
/// introduire un « v2 » un jour sans casser ceux qui lisent encore le premier.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class WebhookSignature
{
    public const string HeaderName = "X-HBA-Signature";

    /// <summary>En-tête portant l'identifiant d'événement, pour la déduplication côté partenaire.</summary>
    public const string EventIdHeaderName = "X-HBA-Event-Id";

    /// <summary>En-tête portant le type d'événement, pour aiguiller sans lire le corps.</summary>
    public const string EventTypeHeaderName = "X-HBA-Event-Type";

    /// <summary>
    /// Construit l'en-tête de signature.
    /// </summary>
    /// <param name="payload">Le corps EXACT qui sera transmis, octet pour octet.</param>
    /// <param name="secret">Secret partagé avec le partenaire.</param>
    /// <param name="atUtc">Instant de la signature.</param>
    public static string Build(string payload, string secret, DateTime atUtc)
    {
        var timestamp = new DateTimeOffset(atUtc, TimeSpan.Zero).ToUnixTimeSeconds();

        return $"t={timestamp},v1={Compute(payload, secret, timestamp)}";
    }

    /// <summary>
    /// Le condensat lui-même. Exposé pour que les tests — et un partenaire qui
    /// débogue son intégration — puissent refaire exactement le calcul.
    /// </summary>
    public static string Compute(string payload, string secret, long timestamp)
    {
        // La chaîne signée sépare l'horodatage du corps par un POINT. Sans
        // séparateur, « 1723372800 » + « 42… » et « 172337280 » + « 042… »
        // produiraient la même chaîne, donc la même signature : deux appels
        // différents, indiscernables.
        var signed = $"{timestamp}.{payload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signed));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
