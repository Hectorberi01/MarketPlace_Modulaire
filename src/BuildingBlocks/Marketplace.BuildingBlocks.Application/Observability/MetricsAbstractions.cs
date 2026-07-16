namespace Marketplace.BuildingBlocks.Application.Observability;

/// <summary>
/// Abstractions de métriques métier, exposées ici (BuildingBlocks) pour que les
/// modules puissent les émettre sans dépendre de l'hôte de composition
/// (<c>Marketplace.Api</c>), où vivent les implémentations concrètes basées sur
/// <see cref="System.Diagnostics.Metrics.Meter"/>.
///
/// Règle : jamais de donnée personnelle en paramètre destiné à devenir un label
/// (email, id, IP, token…). Seuls des libellés à faible cardinalité et bornés.
/// </summary>
public interface IPaymentMetrics
{
    void Attempt(string provider, string paymentMethod, string currency);
    void Success(string provider, string paymentMethod, string currency, long amountMinorUnits, double? durationSeconds = null);
    void Failed(string provider, string paymentMethod, string currency, string failureReason);
    void Pending(string provider, string paymentMethod, string currency);
    void Cancelled(string provider, string paymentMethod, string currency);
    void Refund(string provider, string currency, long amountMinorUnits);
    void WebhookError(string provider, string failureReason);
    void WebhookProcessed(string provider, double durationSeconds);
}

/// <summary>Métriques métier de la marketplace (inscriptions, commandes, CA…).</summary>
public interface IMarketplaceBusinessMetrics
{
    void UserRegistered();
    void ProductCreated();
    void CartCreated();
    void CartAbandoned();
    void OrderCreated();
    void OrderCancelled();
    void OrderCompleted(string currency, long revenueMinorUnits = 0, long commissionMinorUnits = 0);
    void SetGauge(string name, long value);
}

/// <summary>Métriques d'authentification &amp; sécurité.</summary>
public interface ISecurityMetrics
{
    void LoginSuccess(string authenticationMethod, string clientType);
    void LoginFailed(string authenticationMethod, string failureReason, string clientType);
    void Registration(string clientType);
    void PasswordReset();
    void AccountLocked(string failureReason);
    void TokenValidationFailed(string failureReason);
    void Unauthorized(string route);
    void Forbidden(string route);
    void RateLimited(string route);
}

/// <summary>
/// Santé de l'outbox.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// SANS CETTE MÉTRIQUE, LA LETTRE MORTE SERAIT UNE PERTE SILENCIEUSE.
///
/// Avant, un message empoisonné était rejoué toutes les 5 secondes, pour toujours :
/// intenable, mais BRUYANT — ça finissait par se voir. En posant un plafond de
/// tentatives, on a supprimé le bruit. Si l'on ne remplace pas ce bruit par un SIGNAL,
/// on a simplement échangé une boucle visible contre un échec invisible — et c'est pire.
///
/// Une lettre morte n'est pas un incident technique : c'est un fait métier qui ne se
/// produira jamais. Un e-mail de réinitialisation jamais envoyé, un gain vendeur jamais
/// crédité. Cela doit réveiller quelqu'un.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IOutboxMetrics
{
    /// <summary>Une tentative de publication a échoué (sera réessayée).</summary>
    void PublishFailed(string module, string eventType);

    /// <summary>
    /// Un message a épuisé ses tentatives et ne sera PLUS JAMAIS traité.
    /// C'est cette métrique qui doit déclencher une alerte : voir OutboxDeadLetter
    /// dans prometheus-rules.yml.
    /// </summary>
    void DeadLettered(string module, string eventType);
}
