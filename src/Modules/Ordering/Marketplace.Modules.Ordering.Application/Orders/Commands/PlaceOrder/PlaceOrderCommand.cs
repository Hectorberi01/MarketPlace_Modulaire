using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Ordering.Application.Orders.Commands.PlaceOrder;

/// <summary>Place une commande à partir du panier actif de l'acheteur (déclenche le Saga).</summary>
public sealed record PlaceOrderCommand(Guid BuyerId, ShippingAddressInput? ShippingAddress = null, decimal ShippingFee = 0m) : ICommand<Guid>;

/// <summary>Adresse de livraison choisie au checkout (figée sur la commande).</summary>
public sealed record ShippingAddressInput(
    string? Label, string? Recipient, string? Phone,
    string? CommuneCode, string? Quartier, string? Landmark, string? Line1, string? CountryCode,
    double? Latitude, double? Longitude);
