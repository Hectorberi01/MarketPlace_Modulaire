using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.ApproveKyb;

/// <summary>Valide le KYB d'un vendeur (modération, Admin).</summary>
public sealed record ApproveKybCommand(Guid SellerId) : ICommand;
