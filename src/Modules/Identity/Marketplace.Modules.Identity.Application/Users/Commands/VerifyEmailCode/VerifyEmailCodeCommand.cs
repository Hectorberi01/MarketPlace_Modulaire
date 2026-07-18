using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Users.Commands.VerifyEmailCode;

/// <summary>
/// Vérifie un code e-mail à 6 chiffres et marque l'adresse comme vérifiée.
///
/// À la différence de <c>ConfirmEmailCommand</c> (lien, idempotent), la
/// vérification est STRICTE : le code doit correspondre même si l'e-mail est déjà
/// vérifié — c'est ce qui prouve la possession de la boîte lors de l'auto-inscription
/// vendeur. N'active PAS le compte (voir <c>ApproveUserCommand</c>).
/// </summary>
public sealed record VerifyEmailCodeCommand(Guid UserId, string Code) : ICommand;
