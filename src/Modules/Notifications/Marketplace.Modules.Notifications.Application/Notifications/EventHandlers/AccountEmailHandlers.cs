using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Identity.Contracts.IntegrationEvents;
using Marketplace.Modules.Notifications.Application.Abstractions;
using Marketplace.Modules.Notifications.Application.Emails;

namespace Marketplace.Modules.Notifications.Application.Notifications.EventHandlers;

/// <summary>
/// Envoie l'e-mail de vérification d'adresse à l'inscription.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE HANDLER N'EXISTAIT PAS. AUCUN COMPTE N'A JAMAIS REÇU SON LIEN DE VÉRIFICATION.
///
/// `EmailVerificationRequestedIntegrationEvent` était publié consciencieusement à chaque
/// inscription, avec le jeton, depuis le premier jour. Et il n'avait AUCUN consommateur.
/// L'événement partait dans l'outbox, était marqué traité, et disparaissait.
///
/// MediatR et le dispatcher d'événements d'intégration résolvent LAZILY : un événement
/// sans handler ne provoque aucune erreur, aucun avertissement, rien. Il est simplement
/// ignoré — en silence. C'est le mode de défaillance le plus coûteux de cette
/// architecture : le code a l'air complet, et il ne fait rien.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SendEmailVerificationHandler : IIntegrationEventHandler<EmailVerificationRequestedIntegrationEvent>
{
    private readonly IEmailSender _email;
    private readonly ILogger<SendEmailVerificationHandler> _logger;

    public SendEmailVerificationHandler(
        IEmailSender email,
        ILogger<SendEmailVerificationHandler> logger)
    {
        _email = email;
        _logger = logger;
    }

    public async Task HandleAsync(
        EmailVerificationRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // Le champ VerificationToken porte désormais le CODE numérique à 6 chiffres.
        var message = AccountEmailTemplates.EmailVerificationCode(
            integrationEvent.Email, integrationEvent.FirstName, integrationEvent.VerificationToken);

        // On NE capture PAS l'exception. Un échec laisse le message d'outbox non traité,
        // donc rejoué au tour suivant. Avaler l'erreur perdrait définitivement l'e-mail —
        // et l'utilisateur resterait bloqué à la porte, sans que personne ne le sache.
        await _email.SendAsync(message, cancellationToken);

        // On journalise l'utilisateur, JAMAIS l'URL : elle contient le jeton.
        _logger.LogInformation(
            "E-mail de vérification envoyé à l'utilisateur {UserId}.", integrationEvent.UserId);
    }
}

/// <summary>
/// Envoie l'e-mail de réinitialisation de mot de passe.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// C'EST L'EXISTENCE DE CE HANDLER QUI REND LA PLATEFORME SÛRE.
///
/// Faute de canal e-mail, le jeton de réinitialisation n'avait nulle part où aller. Il a
/// donc été renvoyé dans la RÉPONSE HTTP d'un endpoint ANONYME
/// (`POST /mobile/auth/password/forgot`) — avec un « TODO production » en guise
/// d'excuse. N'importe qui saisissait l'e-mail d'un administrateur, lisait son jeton, et
/// prenait son compte.
///
/// Le jeton a maintenant un chemin légitime : Identity → outbox → ici → boîte mail du
/// propriétaire. Et de personne d'autre.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SendPasswordResetEmailHandler : IIntegrationEventHandler<PasswordResetRequestedIntegrationEvent>
{
    private readonly IEmailSender _email;
    private readonly ILogger<SendPasswordResetEmailHandler> _logger;

    public SendPasswordResetEmailHandler(
        IEmailSender email,
        ILogger<SendPasswordResetEmailHandler> logger)
    {
        _email = email;
        _logger = logger;
    }

    public async Task HandleAsync(
        PasswordResetRequestedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // Le champ ResetToken porte désormais le CODE numérique à 6 chiffres.
        var message = AccountEmailTemplates.PasswordResetCode(
            integrationEvent.Email, integrationEvent.FirstName, integrationEvent.ResetToken);

        await _email.SendAsync(message, cancellationToken);

        // NI le jeton, NI l'URL (qui le contient), NI l'e-mail ne sont journalisés ici.
        // Un jeton dans les logs est un jeton lisible par quiconque a accès aux logs — et
        // ce serait recréer, en plus discret, la fuite qu'on vient de fermer.
        _logger.LogInformation(
            "E-mail de réinitialisation envoyé à l'utilisateur {UserId}.", integrationEvent.UserId);
    }
}
