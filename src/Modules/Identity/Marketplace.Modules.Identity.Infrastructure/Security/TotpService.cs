using OtpNet;
using Marketplace.Modules.Identity.Application.Abstractions;

namespace Marketplace.Modules.Identity.Infrastructure.Security;

/// <summary>Double authentification TOTP (RFC 6238) via Otp.NET.</summary>
internal sealed class TotpService : ITotpService
{
    private const string Issuer = "Marketplace";

    public string GenerateSecret()
    {
        var key = KeyGeneration.GenerateRandomKey(20);
        return Base32Encoding.ToString(key);
    }

    public string BuildOtpAuthUri(string secret, string accountName)
    {
        var label = Uri.EscapeDataString($"{Issuer}:{accountName}");
        var issuer = Uri.EscapeDataString(Issuer);
        return $"otpauth://totp/{label}?secret={secret}&issuer={issuer}&algorithm=SHA1&digits=6&period=30";
    }

    public bool VerifyCode(string secret, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var totp = new Totp(Base32Encoding.ToBytes(secret));
        return totp.VerifyTotp(code.Trim(), out _, VerificationWindow.RfcSpecifiedNetworkDelay);
    }
}
