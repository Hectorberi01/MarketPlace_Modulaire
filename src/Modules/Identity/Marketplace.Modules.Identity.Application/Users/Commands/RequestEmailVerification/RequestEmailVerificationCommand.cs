using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Identity.Application.Users.Commands.RequestEmailVerification;

/// <summary>
/// (Ré)émet un code de vérification e-mail à 6 chiffres pour un compte EXISTANT et
/// l'envoie par e-mail (via l'outbox). Utilisé quand un acheteur déjà inscrit
/// démarre une auto-inscription vendeur : le code prouve qu'il relève la boîte
/// avant qu'on lui rattache une boutique.
/// </summary>
public sealed record RequestEmailVerificationCommand(Guid UserId) : ICommand;
