using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.RejectKyb;

/// <summary>Rejette le KYB d'un vendeur (modération, Admin).</summary>
public sealed record RejectKybCommand(Guid SellerId) : ICommand;
