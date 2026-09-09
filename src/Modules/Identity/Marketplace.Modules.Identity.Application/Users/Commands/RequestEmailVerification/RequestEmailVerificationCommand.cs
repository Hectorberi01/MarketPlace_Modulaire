using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Users.Commands.RequestEmailVerification;

/// <summary>
/// (Ré)émet un code de vérification e-mail à 6 chiffres pour un compte EXISTANT et
/// l'envoie par e-mail (via l'outbox). Utilisé quand un acheteur déjà inscrit
/// démarre une auto-inscription vendeur : le code prouve qu'il relève la boîte
/// avant qu'on lui rattache une boutique.
/// </summary>
public sealed record RequestEmailVerificationCommand(Guid UserId) : ICommand
{
    /// <summary>
    /// Délai minimal entre deux envois pour un même compte.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// PUBLIC, PARCE QUE TROIS ENDROITS DOIVENT DIRE LE MÊME NOMBRE.
    ///
    /// Le handler l'applique ; les deux routes de renvoi l'annoncent à l'application,
    /// qui s'en sert pour désactiver son bouton. Tant que la valeur était privée au
    /// handler, les messages affichés étaient des nombres écrits à la main quelque part
    /// — et le jour où le délai change, deux d'entre eux mentent.
    ///
    /// C'est cette désynchronisation qui a produit le défaut d'origine, sous une autre
    /// forme : le serveur savait qu'il n'enverrait rien, et répondait « envoyé ».
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
}
