using Microsoft.Extensions.Logging;
using Marketplace.Modules.Notifications.Application.Abstractions;

namespace Marketplace.Modules.Notifications.Infrastructure.Email;

/// <summary>
/// Adaptateur de DÉVELOPPEMENT : n'envoie rien, écrit l'e-mail dans la console.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// ⚠️ CET ADAPTATEUR ÉCRIT DES JETONS EN CLAIR DANS LES LOGS. C'EST ASSUMÉ — ET
///     STRICTEMENT RÉSERVÉ AU DÉVELOPPEMENT.
///
/// Sans lui, un développeur local ne pourrait ni vérifier un compte ni réinitialiser un
/// mot de passe : le lien serait produit, puis jeté. Il verrait un système qui « marche »
/// sans jamais pouvoir en franchir la première porte. C'est exactement le vide qui a
/// conduit quelqu'un à renvoyer le jeton dans la réponse HTTP — et à ouvrir la plateforme
/// à qui voulait.
///
/// La différence tient à la GARDE : `NotificationsModuleInstaller` REFUSE DE DÉMARRER en
/// Production si l'e-mail n'est pas configuré. Cet adaptateur ne peut donc jamais s'y
/// retrouver par distraction — le processus s'arrête avant.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DevelopmentEmailSender : IEmailSender
{
    private readonly ILogger<DevelopmentEmailSender> _logger;

    public DevelopmentEmailSender(ILogger<DevelopmentEmailSender> logger) => _logger = logger;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "[E-MAIL NON ENVOYÉ — MODE DÉVELOPPEMENT]\n" +
            "  À      : {To}\n" +
            "  Sujet  : {Subject}\n" +
            "  Texte  :\n{TextBody}\n" +
            "  (Configurer Notifications:Email pour un envoi réel.)",
            message.To, message.Subject, message.TextBody);

        return Task.CompletedTask;
    }
}
