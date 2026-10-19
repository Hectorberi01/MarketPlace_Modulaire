namespace Marketplace.Host.Observability.Metrics;

/// <summary>
/// Noms des <see cref="System.Diagnostics.Metrics.Meter"/> personnalisés de la
/// plateforme. Ils doivent être enregistrés auprès d'OpenTelemetry via
/// <c>metrics.AddMeter(...)</c> (fait dans <c>ObservabilityExtensions</c>) pour
/// que leurs instruments soient exportés (OTLP → Collector → Prometheus).
///
/// Convention de nommage : on suit les conventions Prometheus (snake_case,
/// suffixe <c>_total</c> pour les compteurs, <c>_seconds</c>/<c>_bytes</c> pour les
/// unités). OpenTelemetry ajoute automatiquement le suffixe <c>_total</c> aux
/// compteurs monotones à l'export Prometheus : on nomme donc les compteurs SANS
/// <c>_total</c> ici (ex. <c>payment_attempts</c> → exposé <c>payment_attempts_total</c>).
/// </summary>
public static class MarketplaceMeters
{
    /// <summary>Disponibilité / santé de l'application (api_up, uptime, dépendances).</summary>
    public const string Availability = "Marketplace.Availability";

    /// <summary>Métriques métier des paiements (par fournisseur / devise / statut).</summary>
    public const string Payments = "Marketplace.Payments";

    /// <summary>Métriques métier de la marketplace (inscriptions, commandes, CA…).</summary>
    public const string Business = "Marketplace.Business";

    /// <summary>Authentification &amp; sécurité (connexions, 401/403, rate limiting…).</summary>
    public const string Security = "Marketplace.Security";

    /// <summary>Santé de l'outbox (échecs de publication, lettres mortes).</summary>
    public const string Outbox = "Marketplace.Outbox";

    /// <summary>Tous les meters custom — pratique pour l'enregistrement en masse.</summary>
    public static readonly string[] All = { Availability, Payments, Business, Security, Outbox };
}
