using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.RequestSellerReactivation;

/// <summary>Le vendeur (compte fermé) demande la réactivation de son compte.</summary>
public sealed record RequestSellerReactivationCommand(Guid SellerId) : ICommand;
