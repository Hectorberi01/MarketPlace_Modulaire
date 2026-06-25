using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.SuspendSeller;

/// <summary>Suspend un vendeur (Admin).</summary>
public sealed record SuspendSellerCommand(Guid SellerId) : ICommand;
