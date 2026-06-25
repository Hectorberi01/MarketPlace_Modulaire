namespace Marketplace.Modules.Identity.Application.Abstractions;

/// <summary>
/// Service de double authentification basé TOTP (RFC 6238), implémenté avec
/// Otp.NET en Infrastructure.
/// </summary>
public interface ITotpService
{
    /// <summary>Génère un secret partagé (base32) à présenter sous forme de QR code.</summary>
    string GenerateSecret();

    /// <summary>Construit l'URI otpauth:// pour les applications d'authentification.</summary>
    string BuildOtpAuthUri(string secret, string accountName);

    /// <summary>Vérifie un code TOTP à 6 chiffres contre le secret.</summary>
    bool VerifyCode(string secret, string code);
}
