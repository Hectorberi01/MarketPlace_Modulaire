using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Ordering.Application.Orders.Commands.PlaceOrder;

/// <summary>
/// Place une commande à partir du panier actif de l'acheteur (déclenche le Saga).
///
/// <paramref name="WalletAmount"/> est la part que l'acheteur CHOISIT de régler avec sa
/// cagnotte — zéro par défaut, et c'est important : la cagnotte ne se dépense jamais
/// d'elle-même. Un client qui met de côté un remboursement pour un achat précis ne doit
/// pas le voir fondre sur la commande d'après. Le montant est un plafond, pas une
/// promesse : il est ramené au solde réellement disponible et au total de la commande.
/// </summary>
public sealed record PlaceOrderCommand(
    Guid BuyerId,
    ShippingAddressInput? ShippingAddress = null,
    decimal ShippingFee = 0m,
    decimal WalletAmount = 0m) : ICommand<Guid>;

/// <summary>Adresse de livraison choisie au checkout (figée sur la commande).</summary>
public sealed record ShippingAddressInput(
    string? Label, string? Recipient, string? Phone,
    string? CommuneCode, string? Quartier, string? Landmark, string? Line1, string? CountryCode,
    double? Latitude, double? Longitude);
