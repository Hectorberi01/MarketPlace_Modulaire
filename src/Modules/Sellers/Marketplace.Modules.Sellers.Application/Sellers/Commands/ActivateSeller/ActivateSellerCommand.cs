using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.ActivateSeller;

/// <summary>Active un vendeur (KYB validé + payout requis). Admin.</summary>
public sealed record ActivateSellerCommand(Guid SellerId) : ICommand;
