using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.RejectKyb;

/// <summary>
/// Rejette le KYB d'un vendeur (modération, Admin).
/// </summary>
/// <param name="Reason">
/// Motif du refus, transmis tel quel au vendeur par notification et par e-mail.
/// Obligatoire : c'est la seule chose qui lui permette de corriger son dossier.
/// </param>
public sealed record RejectKybCommand(Guid SellerId, string Reason) : ICommand;
