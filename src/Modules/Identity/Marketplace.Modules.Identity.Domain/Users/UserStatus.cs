namespace Marketplace.Modules.Identity.Domain.Users;

/// <summary>Statut d'un compte (cf. dossier, User).</summary>
public enum UserStatus
{
    PendingVerification = 0,
    Active = 1,
    Suspended = 2
}
