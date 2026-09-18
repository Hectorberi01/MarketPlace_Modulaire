using Marketplace.Modules.Notifications.Application.Abstractions;

namespace Marketplace.Modules.Notifications.Infrastructure.Email;

/// <summary>
/// Construit les liens des e-mails de compte à partir de <see cref="EmailOptions.AppBaseUrl"/>.
///
/// Les liens pointent vers le FRONT WEB, pas vers l'API — et ce n'est pas un détail.
///
/// Un lien de réinitialisation ne doit JAMAIS être une URL qui réinitialise. Les clients de
/// messagerie et les passerelles antivirus PRÉ-CHARGENT les liens des e-mails pour les
/// analyser : une URL à effet de bord serait déclenchée avant même que l'utilisateur ne
/// clique. Le lien mène donc à une PAGE, qui affiche un formulaire, qui appelle ensuite
/// `POST /auth/password/reset`. La lecture est sûre ; seule l'écriture agit.
/// </summary>
public sealed class AccountLinkBuilder : IAccountLinkBuilder
{
    private readonly EmailOptions _options;

    public AccountLinkBuilder(EmailOptions options) => _options = options;

    public string EmailVerification(Guid userId, string token)
        => _options.Link($"verifier-email?uid={Uri.EscapeDataString(userId.ToString())}&token={Uri.EscapeDataString(token)}");

    public string PasswordReset(string email, string token)
        => _options.Link($"reinitialiser-mot-de-passe?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}");
}
