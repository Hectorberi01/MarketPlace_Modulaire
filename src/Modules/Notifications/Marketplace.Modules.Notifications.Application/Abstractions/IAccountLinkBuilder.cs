namespace Marketplace.Modules.Notifications.Application.Abstractions;

/// <summary>
/// Fabrique les liens cliquables des e-mails de compte.
///
/// Pourquoi un port, et pas juste une chaîne de configuration lue dans le handler :
/// l'URL publique de l'application est une préoccupation d'INFRASTRUCTURE (elle change
/// selon l'environnement), alors que « quel lien mettre dans l'e-mail de vérification »
/// est une décision APPLICATIVE. Le port sépare les deux, et rend les handlers testables
/// sans configuration.
/// </summary>
public interface IAccountLinkBuilder
{
    /// <summary>Lien de confirmation d'adresse e-mail.</summary>
    string EmailVerification(Guid userId, string token);

    /// <summary>Lien de choix d'un nouveau mot de passe.</summary>
    string PasswordReset(string email, string token);
}
