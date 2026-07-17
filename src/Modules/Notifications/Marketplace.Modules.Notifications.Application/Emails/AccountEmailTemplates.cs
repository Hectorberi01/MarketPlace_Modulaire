using System.Net;
using Marketplace.Modules.Notifications.Application.Abstractions;

namespace Marketplace.Modules.Notifications.Application.Emails;

/// <summary>
/// Gabarits des e-mails de compte (vérification, réinitialisation).
///
/// Volontairement pauvres en HTML : pas d'images, pas de CSS externe, pas de webfont. Un
/// e-mail transactionnel doit s'afficher partout — y compris dans un client mobile qui
/// ampute le CSS — et un e-mail chargé passe plus souvent en spam. Or un e-mail de
/// réinitialisation en spam, c'est un utilisateur enfermé dehors.
///
/// Chaque message a une version TEXTE complète, avec l'URL en clair : c'est elle qui sauve
/// la mise quand le client mail bloque le HTML.
/// </summary>
public static class AccountEmailTemplates
{
    public static EmailMessage EmailVerification(string to, string firstName, string verificationUrl)
    {
        var name = WebUtility.HtmlEncode(firstName);
        var url = WebUtility.HtmlEncode(verificationUrl);

        var html = $"""
            <div style="font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;font-size:16px;color:#111;line-height:1.6">
              <p>Bonjour {name},</p>
              <p>Bienvenue sur HBA Express. Confirmez votre adresse e-mail pour activer votre compte :</p>
              <p style="margin:28px 0">
                <a href="{url}" style="background:#111;color:#fff;padding:12px 22px;border-radius:6px;text-decoration:none;display:inline-block">
                  Confirmer mon adresse
                </a>
              </p>
              <p style="color:#555;font-size:14px">Ou copiez ce lien dans votre navigateur :<br>{url}</p>
              <p style="color:#555;font-size:14px">Ce lien expire dans 48 heures.</p>
              <p style="color:#555;font-size:14px">Si vous n'avez pas créé de compte, ignorez cet e-mail.</p>
            </div>
            """;

        var text = $"""
            Bonjour {firstName},

            Bienvenue sur HBA Express. Confirmez votre adresse e-mail pour activer votre compte :

            {verificationUrl}

            Ce lien expire dans 48 heures.
            Si vous n'avez pas créé de compte, ignorez cet e-mail.
            """;

        return new EmailMessage(to, "Confirmez votre adresse e-mail — HBA Express", html, text);
    }

    public static EmailMessage PasswordReset(string to, string firstName, string resetUrl)
    {
        var name = WebUtility.HtmlEncode(firstName);
        var url = WebUtility.HtmlEncode(resetUrl);

        var html = $"""
            <div style="font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;font-size:16px;color:#111;line-height:1.6">
              <p>Bonjour {name},</p>
              <p>Vous avez demandé à réinitialiser votre mot de passe HBA Express.</p>
              <p style="margin:28px 0">
                <a href="{url}" style="background:#111;color:#fff;padding:12px 22px;border-radius:6px;text-decoration:none;display:inline-block">
                  Choisir un nouveau mot de passe
                </a>
              </p>
              <p style="color:#555;font-size:14px">Ou copiez ce lien dans votre navigateur :<br>{url}</p>
              <p style="color:#555;font-size:14px"><strong>Ce lien expire dans 1 heure</strong> et ne peut servir qu'une fois.</p>
              <p style="color:#555;font-size:14px">
                Si vous n'êtes pas à l'origine de cette demande, ignorez cet e-mail : votre mot de passe
                reste inchangé.
              </p>
            </div>
            """;

        var text = $"""
            Bonjour {firstName},

            Vous avez demandé à réinitialiser votre mot de passe HBA Express.
            Choisissez un nouveau mot de passe ici :

            {resetUrl}

            Ce lien expire dans 1 heure et ne peut servir qu'une fois.

            Si vous n'êtes pas à l'origine de cette demande, ignorez cet e-mail :
            votre mot de passe reste inchangé.
            """;

        return new EmailMessage(to, "Réinitialisation de votre mot de passe — HBA Express", html, text);
    }
}
