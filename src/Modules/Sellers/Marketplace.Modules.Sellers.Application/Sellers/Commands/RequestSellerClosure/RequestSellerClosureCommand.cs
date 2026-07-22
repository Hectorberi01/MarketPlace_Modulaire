using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.RequestSellerClosure;

/// <summary>Fermeture du compte demandée par le vendeur (suppression partielle).</summary>
public sealed record RequestSellerClosureCommand(Guid SellerId) : ICommand;
