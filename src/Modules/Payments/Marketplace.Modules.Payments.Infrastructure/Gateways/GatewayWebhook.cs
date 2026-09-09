using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;

namespace Marketplace.Modules.Payments.Infrastructure.Gateways;

/// <summary>
/// Vérification de signature et parsing normalisé d'un payload de webhook PSP,
/// mutualisés entre les adaptateurs simulés et les adaptateurs HTTP réels.
/// </summary>
public static class GatewayWebhook
{
    private static readonly JsonDocumentOptions JsonOptions = new() { AllowTrailingCommas = true };

    /// <summary>
    /// Autorise l'acceptation d'un webhook NON signé uniquement lorsqu'aucun secret
    /// n'est configuré (tests locaux / sandbox). <c>false</c> par défaut : en
    /// production, un secret manquant fait REJETER le webhook (pas de fail-open,
    /// qui permettrait de créditer une commande via un faux webhook). À positionner
    /// une seule fois au démarrage, à <c>true</c> en environnement Development.
    /// </summary>
    public static bool AllowUnsignedWhenSecretMissing { get; set; }

    /// <summary>Signature HMAC-SHA256 du corps brut, en hexadécimal minuscule.</summary>
    public static string ComputeSignature(string rawBody, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Vérifie la signature. Secret vide : accepté SANS signature uniquement si
    /// <see cref="AllowUnsignedWhenSecretMissing"/> est activé (Development). En
    /// production (défaut), un secret manquant fait rejeter le webhook.
    /// </summary>
    public static bool VerifySignature(string rawBody, string? signatureHeader, string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return AllowUnsignedWhenSecretMissing;
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return false;
        }

        var expected = ComputeSignature(rawBody, secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signatureHeader.Trim()));
    }

    /// <summary>
    /// Tolérance d'horloge acceptée sur l'horodatage d'un webhook signé.
    ///
    /// Cinq minutes : assez large pour absorber une dérive d'horloge serveur et une
    /// nouvelle tentative du prestataire, assez étroit pour qu'un corps capturé cesse
    /// vite d'être rejouable. Même valeur que la signature du service coursier, et
    /// c'est délibéré : deux fenêtres différentes seraient deux raisonnements à tenir.
    /// </summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>
    /// L'horodatage d'un webhook signé est-il dans la fenêtre acceptable ?
    ///
    /// Un horodatage ILLISIBLE est refusé, jamais ignoré : il n'y a aucune raison
    /// légitime pour qu'un prestataire envoie autre chose qu'un temps Unix en
    /// secondes, et « je ne comprends pas, donc j'accepte » est la forme la plus
    /// courante du fail-open.
    /// </summary>
    public static bool IsWithinClockSkew(string? unixSeconds, DateTimeOffset? now = null)
    {
        if (!long.TryParse(unixSeconds, out var seconds))
        {
            return false;
        }

        var sent = DateTimeOffset.FromUnixTimeSeconds(seconds);
        var reference = now ?? DateTimeOffset.UtcNow;

        // Valeur absolue : une horloge prestataire EN AVANCE est aussi suspecte
        // qu'un rejeu tardif, et l'accepter rendrait la fenêtre unilatérale.
        return (reference - sent).Duration() <= MaxClockSkew;
    }

    /// <summary>
    /// Vérifie la signature puis normalise le payload : lit le type d'événement
    /// dans <paramref name="eventTypeField"/>, mappe le résultat et extrait la
    /// référence de corrélation.
    /// </summary>
    public static GatewayEvent Parse(
        string rawBody,
        string? signatureHeader,
        string secret,
        string eventTypeField,
        Func<string, GatewayOutcome> mapOutcome,
        Func<JsonElement, string?> extractReference)
    {
        if (!VerifySignature(rawBody, signatureHeader, secret))
        {
            return new GatewayEvent(Verified: false, GatewayOutcome.Ignored, null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBody) ? "{}" : rawBody, JsonOptions);
            var root = document.RootElement;

            var eventType = root.TryGetProperty(eventTypeField, out var typeEl) ? typeEl.GetString() ?? string.Empty : string.Empty;
            var reference = extractReference(root);
            var outcome = mapOutcome(eventType);

            return new GatewayEvent(Verified: true, outcome, reference, FailureReason: outcome == GatewayOutcome.Failed ? eventType : null);
        }
        catch (JsonException)
        {
            return new GatewayEvent(Verified: false, GatewayOutcome.Ignored, null, "Payload JSON invalide.");
        }
    }
}
