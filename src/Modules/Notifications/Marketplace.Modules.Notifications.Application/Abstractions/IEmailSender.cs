namespace Marketplace.Modules.Notifications.Application.Abstractions;

/// <summary>Un e-mail transactionnel : destinataire, sujet, corps HTML, et sa version texte.</summary>
/// <param name="To">Adresse du destinataire.</param>
/// <param name="Subject">Sujet.</param>
/// <param name="HtmlBody">Corps HTML.</param>
/// <param name="TextBody">
/// Corps texte brut. <b>Obligatoire, pas facultatif.</b> Un e-mail sans partie texte est
/// noté comme suspect par la plupart des filtres anti-spam — et un e-mail de
/// réinitialisation qui tombe en spam est un utilisateur définitivement enfermé dehors.
/// </param>
public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Port d'envoi d'e-mails transactionnels.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE PORT N'EXISTAIT PAS. AUCUN E-MAIL N'A JAMAIS PU PARTIR DE CETTE PLATEFORME.
///
/// Le module Notifications ne savait faire que du push (FCM) et de l'in-app. Aucune
/// abstraction e-mail, aucun SMTP, aucun fournisseur. Conséquences en chaîne :
///
///   • `EmailVerificationRequestedIntegrationEvent` était publié… et consommé par
///     personne. Aucun compte n'a jamais reçu son lien de vérification.
///   • La réinitialisation de mot de passe n'avait nulle part où envoyer son jeton — et
///     quelqu'un a « résolu » le problème en le RENVOYANT DANS LA RÉPONSE HTTP d'un
///     endpoint anonyme. C'était la prise de contrôle de n'importe quel compte.
///
/// Le trou fonctionnel avait donc engendré un trou de sécurité. C'est fréquent : une
/// capacité manquante finit toujours par être contournée, et le contournement est
/// rarement sûr.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Envoie l'e-mail. Lève en cas d'échec (réseau, 4xx/5xx du fournisseur).
    ///
    /// <b>Lever est le comportement voulu.</b> Ces envois se font depuis
    /// l'OutboxProcessor : une exception laisse le message non traité, donc il sera REJOUÉ
    /// après temporisation (backoff exponentiel, 10 tentatives sur ~2 h). Passé ce délai, le
    /// message part en LETTRE MORTE — visible, alertée, rejouable à la main. Avaler l'erreur
    /// ici reviendrait au contraire à perdre l'e-mail DÉFINITIVEMENT et EN SILENCE : un
    /// utilisateur resterait enfermé dehors sans que personne ne le sache.
    /// </summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
