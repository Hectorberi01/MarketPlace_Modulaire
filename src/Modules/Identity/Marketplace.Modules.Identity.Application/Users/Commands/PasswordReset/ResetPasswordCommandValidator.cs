using FluentValidation;

namespace Marketplace.Modules.Identity.Application.Users.Commands.PasswordReset;

/// <summary>
/// Validation d'entrée de la réinitialisation de mot de passe.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE VALIDATEUR N'EXISTAIT PAS, ET C'ÉTAIT LA SEULE ROUTE SANS RÈGLE DE MOT DE PASSE.
///
/// `RegisterUserCommandValidator` et `ChangePasswordCommandValidator` imposent tous
/// deux 8 à 128 caractères. Le dossier `PasswordReset/` ne contenait que la commande et
/// son handler. Le seul contrôle en aval portait sur le HASH — `string.IsNullOrWhiteSpace(
/// newPasswordHash)` dans `User.ResetPassword` — c'est-à-dire sur une chaîne que BCrypt
/// vient de produire, et qui n'est donc jamais vide, quel que soit le mot de passe.
///
/// La route de réinitialisation acceptait donc `NewPassword = "a"`. Et elle RÉVOQUE
/// TOUTES LES SESSIONS au passage : ce mot de passe d'un caractère devenait aussitôt la
/// seule voie d'accès au compte.
///
/// Le trou venait de la symétrie manquante, pas d'un choix : deux chemins mènent à un
/// nouveau mot de passe, un seul avait sa règle. C'est le motif à retenir — chercher
/// les validateurs ABSENTS, pas seulement relire ceux qui existent.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty();
        RuleFor(c => c.Token).NotEmpty();

        // Mêmes bornes que l'inscription et le changement de mot de passe. Les trois
        // doivent bouger ensemble : un utilisateur ne comprendrait pas qu'un mot de passe
        // accepté à l'inscription soit refusé à la réinitialisation.
        RuleFor(c => c.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}
