using System.Diagnostics.Metrics;
using Marketplace.BuildingBlocks.Application.Observability;

namespace Marketplace.Host.Observability.Metrics;

/// <summary>
/// Métriques d'authentification &amp; sécurité (meter <see cref="MarketplaceMeters.Security"/>).
///
/// Labels autorisés (faible cardinalité, non personnels) : <c>authentication_method</c>,
/// <c>failure_reason</c>, <c>client_type</c>, <c>route</c> (motif de route, jamais l'URL
/// complète). INTERDIT en label : email, identifiant utilisateur, IP complète, token,
/// téléphone, session id → pour l'analyse par IP, utiliser Loki + logs structurés.
/// </summary>
public sealed class SecurityMetrics : ISecurityMetrics, IDisposable
{
    private readonly Meter _meter;

    private readonly Counter<long> _loginSuccess;
    private readonly Counter<long> _loginFailed;
    private readonly Counter<long> _registrations;
    private readonly Counter<long> _passwordResets;
    private readonly Counter<long> _unauthorized;   // 401
    private readonly Counter<long> _forbidden;       // 403
    private readonly Counter<long> _rateLimited;     // 429
    private readonly Counter<long> _accountLocked;
    private readonly Counter<long> _tokenValidationFailed;

    public SecurityMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MarketplaceMeters.Security);

        _loginSuccess = _meter.CreateCounter<long>("login_success", description: "Connexions réussies.");
        _loginFailed = _meter.CreateCounter<long>("login_failed", description: "Connexions échouées.");
        _registrations = _meter.CreateCounter<long>("user_registration", description: "Inscriptions.");
        _passwordResets = _meter.CreateCounter<long>("password_reset", description: "Réinitialisations de mot de passe.");
        _unauthorized = _meter.CreateCounter<long>("unauthorized_requests", description: "Requêtes rejetées 401.");
        _forbidden = _meter.CreateCounter<long>("forbidden_requests", description: "Requêtes rejetées 403.");
        _rateLimited = _meter.CreateCounter<long>("rate_limit_exceeded", description: "Requêtes bloquées par rate limiting (429).");
        _accountLocked = _meter.CreateCounter<long>("account_locked", description: "Comptes verrouillés.");
        _tokenValidationFailed = _meter.CreateCounter<long>("token_validation_failed", description: "Échecs de validation de token.");
    }

    public void LoginSuccess(string authenticationMethod, string clientType) =>
        _loginSuccess.Add(1,
            new KeyValuePair<string, object?>("authentication_method", authenticationMethod),
            new KeyValuePair<string, object?>("client_type", clientType));

    public void LoginFailed(string authenticationMethod, string failureReason, string clientType) =>
        _loginFailed.Add(1,
            new KeyValuePair<string, object?>("authentication_method", authenticationMethod),
            new KeyValuePair<string, object?>("failure_reason", failureReason),
            new KeyValuePair<string, object?>("client_type", clientType));

    public void Registration(string clientType) =>
        _registrations.Add(1, new KeyValuePair<string, object?>("client_type", clientType));

    public void PasswordReset() => _passwordResets.Add(1);

    public void AccountLocked(string failureReason) =>
        _accountLocked.Add(1, new KeyValuePair<string, object?>("failure_reason", failureReason));

    public void TokenValidationFailed(string failureReason) =>
        _tokenValidationFailed.Add(1, new KeyValuePair<string, object?>("failure_reason", failureReason));

    // --- Émis par le middleware (voir SecurityMetricsMiddleware) ---

    public void Unauthorized(string route) =>
        _unauthorized.Add(1, new KeyValuePair<string, object?>("route", route));

    public void Forbidden(string route) =>
        _forbidden.Add(1, new KeyValuePair<string, object?>("route", route));

    public void RateLimited(string route) =>
        _rateLimited.Add(1, new KeyValuePair<string, object?>("route", route));

    public void Dispose() => _meter.Dispose();
}
