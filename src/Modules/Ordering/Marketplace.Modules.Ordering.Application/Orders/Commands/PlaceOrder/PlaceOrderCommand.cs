using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Ordering.Application.Orders.Commands.PlaceOrder;

/// <summary>Place une commande à partir du panier actif de l'acheteur (déclenche le Saga).</summary>
public sealed record PlaceOrderCommand(Guid BuyerId) : ICommand<Guid>;
