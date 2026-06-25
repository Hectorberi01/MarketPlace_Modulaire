namespace Marketplace.Modules.Identity.Application.Abstractions;

/// <summary>
/// Durées de vie des jetons, fournies par la configuration (implémenté en
/// Infrastructure). Permet aux handlers de calculer les expirations sans
/// dépendre directement de la config.
/// </summary>
public interface IAuthTokenSettings
{
    TimeSpan RefreshTokenLifetime { get; }

    TimeSpan EmailVerificationLifetime { get; }
}
