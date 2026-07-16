using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Users.Commands.PasswordReset;

/// <summary>
/// Demande de réinitialisation du mot de passe. Génère un jeton à usage unique (valide 1 h),
/// n'en garde que le HACHÉ en base, et publie le jeton en clair dans un événement
/// d'intégration — que le module Notifications transforme en e-mail.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// ⚠️ CETTE COMMANDE NE RENVOIE RIEN. C'EST UN CORRECTIF DE SÉCURITÉ, PAS UN DÉTAIL
///     DE SIGNATURE. NE PAS LUI REDONNER DE VALEUR DE RETOUR.
///
/// Elle renvoyait autrefois `ICommand&lt;string?&gt;` : le jeton EN CLAIR. Et
/// `POST /mobile/auth/password/forgot` — endpoint ANONYME — le recopiait dans sa
/// réponse HTTP :
///
///     // TODO production : envoyer result.Value par e-mail/SMS et ne pas l'exposer ici.
///     return Results.Ok(new { sent = true, token = result.Value });
///
/// Conséquence : n'importe qui, sans compte, saisissait l'e-mail d'un administrateur,
/// lisait son jeton dans la réponse, et changeait son mot de passe. Les cinq hôtes
/// partagent la même base Identity — c'était la PRISE DE CONTRÔLE TOTALE de la
/// plateforme, en deux requêtes.
///
/// Le TODO disait la vérité : il n'existait aucun canal e-mail dans tout le dépôt. Le
/// jeton n'avait donc nulle part où aller… et il est sorti par la porte.
///
/// En supprimant la valeur de retour, la fuite devient IMPOSSIBLE À RÉÉCRIRE : un endpoint
/// ne peut pas divulguer ce qu'il ne reçoit pas. La garantie est portée par le TYPE, pas
/// par la vigilance du prochain développeur.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// <para>
/// Anti-énumération : la commande réussit silencieusement même si l'e-mail est inconnu, et
/// le BFF répond toujours la même chose — on ne peut donc pas distinguer un compte existant
/// d'un compte absent.
/// </para>
/// </summary>
public sealed record RequestPasswordResetCommand(string Email) : ICommand;
