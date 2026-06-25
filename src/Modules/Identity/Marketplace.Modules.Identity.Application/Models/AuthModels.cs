namespace Marketplace.Modules.Identity.Application.Models;

/// <summary>Paire de jetons émise à l'authentification.</summary>
public sealed record AuthTokens(
    string AccessToken,
    DateTime AccessTokenExpiresOnUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresOnUtc);

/// <summary>
/// Résultat d'un login : soit les jetons, soit l'indication qu'un code MFA est
/// requis (l'appelant rappelle alors le login avec le code).
/// </summary>
public sealed record LoginResponse(bool MfaRequired, AuthTokens? Tokens);

/// <summary>Données d'amorçage MFA à présenter à l'utilisateur (QR code).</summary>
public sealed record MfaSetupResponse(string Secret, string OtpAuthUri);
