namespace Marketplace.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Paramètres d'authentification, liés depuis la section « Jwt » de la
/// configuration. La clé de signature doit être longue (>= 32 octets) et secrète.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "marketplace";
    public string Audience { get; set; } = "marketplace";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
    public int EmailVerificationHours { get; set; } = 48;
}
