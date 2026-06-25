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

    /// <summary>Signature HMAC-SHA256 du corps brut, en hexadécimal minuscule.</summary>
    public static string ComputeSignature(string rawBody, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Vérifie la signature. Secret vide = sandbox permissif (on accepte sans
    /// signature pour faciliter les tests locaux).
    /// </summary>
    public static bool VerifySignature(string rawBody, string? signatureHeader, string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return true;
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
