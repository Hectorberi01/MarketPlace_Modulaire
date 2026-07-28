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

    /// <summary>
    /// Vérification e-mail par CODE numérique (saisi dans l'app mobile), plutôt que
    /// par lien : plus simple sur mobile, et le code ne quitte jamais l'appareil.
    /// </summary>
    public static EmailMessage EmailVerificationCode(string to, string firstName, string code)
    {
        var name = WebUtility.HtmlEncode(firstName);
        var safeCode = WebUtility.HtmlEncode(code);

        var html = $"""
            <div style="font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;font-size:16px;color:#111;line-height:1.6">
              <p>Bonjour {name},</p>
              <p>Bienvenue sur HBA Express. Voici votre code de vérification :</p>
              <p style="margin:28px 0;font-size:32px;font-weight:700;letter-spacing:6px">{safeCode}</p>
              <p style="color:#555;font-size:14px">Saisissez ce code dans l'application pour activer votre compte. Il expire dans 48 heures.</p>
              <p style="color:#555;font-size:14px">Si vous n'avez pas créé de compte, ignorez cet e-mail.</p>
            </div>
            """;

        var text = $"""
            Bonjour {firstName},

            Votre code de vérification HBA Express : {code}

            Saisissez-le dans l'application pour activer votre compte. Il expire dans 48 heures.
            Si vous n'avez pas créé de compte, ignorez cet e-mail.
            """;

        return new EmailMessage(to, "Votre code de vérification — HBA Express", html, text);
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

    /// <summary>
    /// Réinitialisation de mot de passe par CODE numérique (saisi dans l'app),
    /// plutôt que par lien. Cohérent avec la vérification e-mail.
    /// </summary>
    public static EmailMessage PasswordResetCode(string to, string firstName, string code)
    {
        var name = WebUtility.HtmlEncode(firstName);
        var safeCode = WebUtility.HtmlEncode(code);

        var html = $"""
            <div style="font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;font-size:16px;color:#111;line-height:1.6">
              <p>Bonjour {name},</p>
              <p>Vous avez demandé à réinitialiser votre mot de passe HBA Express. Voici votre code :</p>
              <p style="margin:28px 0;font-size:32px;font-weight:700;letter-spacing:6px">{safeCode}</p>
              <p style="color:#555;font-size:14px">Saisissez ce code dans l'application pour choisir un nouveau mot de passe. <strong>Il expire dans 1 heure</strong> et ne peut servir qu'une fois.</p>
              <p style="color:#555;font-size:14px">Si vous n'êtes pas à l'origine de cette demande, ignorez cet e-mail : votre mot de passe reste inchangé.</p>
            </div>
            """;

        var text = $"""
            Bonjour {firstName},

            Votre code de réinitialisation de mot de passe HBA Express : {code}

            Saisissez-le dans l'application pour choisir un nouveau mot de passe.
            Il expire dans 1 heure et ne peut servir qu'une fois.

            Si vous n'êtes pas à l'origine de cette demande, ignorez cet e-mail :
            votre mot de passe reste inchangé.
            """;

        return new EmailMessage(to, "Votre code de réinitialisation — HBA Express", html, text);
    }

    /// <summary>
    /// Boutique VALIDÉE (profil vendeur activé par un administrateur) : le vendeur
    /// peut désormais publier ses produits.
    /// </summary>
    public static EmailMessage SellerActivated(string to, string firstName, string shopName)
    {
        var name = WebUtility.HtmlEncode(firstName);
        var shop = WebUtility.HtmlEncode(shopName);

        var html = $"""
            <div style="font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;font-size:16px;color:#111;line-height:1.6">
              <p>Bonjour {name},</p>
              <p>Bonne nouvelle : votre boutique <strong>{shop}</strong> est <strong>validée</strong> 🎉</p>
              <p>Vous pouvez désormais <strong>publier vos produits</strong> et recevoir des commandes. Ouvrez l'application HbaExpress PRO pour commencer.</p>
              <p style="color:#555;font-size:14px">Merci de faire partie de HBA Express.</p>
            </div>
            """;

        var text = $"""
            Bonjour {firstName},

            Votre boutique {shopName} est validée ! Vous pouvez désormais publier vos
            produits et recevoir des commandes depuis l'application HbaExpress PRO.

            Merci de faire partie de HBA Express.
            """;

        return new EmailMessage(to, "Votre boutique est validée — HBA Express", html, text);
    }

    /// <summary>
    /// Gabarit GÉNÉRIQUE, utilisé pour doubler par e-mail une notification déjà envoyée
    /// en push/in-app (commande confirmée, colis expédié, litige tranché…).
    ///
    /// Pourquoi un gabarit unique plutôt qu'un par événement : le push et l'e-mail
    /// portent exactement le même message. En dupliquer la rédaction garantirait qu'ils
    /// divergent au premier changement de formulation.
    /// </summary>
    public static EmailMessage Transactional(string to, string firstName, string subject, string message)
    {
        var name = WebUtility.HtmlEncode(firstName);
        var title = WebUtility.HtmlEncode(subject);
        var body = WebUtility.HtmlEncode(message);

        var html = $"""
            <div style="font-family:system-ui,-apple-system,Segoe UI,Roboto,sans-serif;font-size:16px;color:#111;line-height:1.6">
              <p>Bonjour {name},</p>
              <p><strong>{title}</strong></p>
              <p>{body}</p>
              <p style="color:#555;font-size:14px">Retrouvez le détail dans l'application HBA Express.</p>
            </div>
            """;

        var text = $"""
            Bonjour {firstName},

            {subject}

            {message}

            Retrouvez le détail dans l'application HBA Express.
            """;

        return new EmailMessage(to, $"{subject} — HBA Express", html, text);
    }
}
