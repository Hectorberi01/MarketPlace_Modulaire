using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Users.Commands.VerifyEmailCode;

/// <summary>
/// Vérifie un code e-mail à 6 chiffres et marque l'adresse comme vérifiée.
///
/// À la différence de <c>ConfirmEmailCommand</c> (idempotente), la vérification est
/// STRICTE : le code doit correspondre même si l'e-mail est déjà vérifié — c'est ce qui
/// prouve la possession de la boîte lors de l'auto-inscription vendeur.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ADRESSE, ET NON L'IDENTIFIANT. C'EST CE QUI FERME L'ORACLE D'ÉNUMÉRATION.
///
/// Tant que cette commande exigeait un Guid, l'écran de saisie du code avait besoin
/// qu'on le lui donne — donc `/seller/auth/register` devait le RENDRE, donc il révélait
/// qu'une adresse a un compte, et son identifiant interne avec. Le Guid n'était pas le
/// problème : c'est la dépendance à ce Guid qui l'était.
///
/// L'application, elle, connaît l'adresse : l'utilisateur vient de la saisir.
///
/// Effet de bord bienvenu : `/seller/auth/verify` n'est plus attaquable avec un
/// identifiant récolté ailleurs. Il faut désormais l'adresse ET le code.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// ACTIVE le compte s'il était encore en attente (voir le handler) : c'est l'instant
/// où l'on sait que l'adresse est réelle.
/// </summary>
public sealed record VerifyEmailCodeCommand(string Email, string Code) : ICommand;
