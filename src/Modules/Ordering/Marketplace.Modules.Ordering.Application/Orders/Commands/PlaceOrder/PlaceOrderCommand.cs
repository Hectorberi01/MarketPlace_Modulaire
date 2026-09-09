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
    decimal WalletAmount = 0m,
    /// <summary>
    /// Le total que l'acheteur A VU quand il a appuyé sur « Payer ».
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LE DEVIS ET LE PAIEMENT SONT DEUX VALORISATIONS INDÉPENDANTES.
    ///
    /// `/checkout/quote` valorise le panier à l'instant T1 ; `/checkout/pay` le
    /// revalorise à T2, en repassant par `PriceCalculator` — qui réévalue
    /// `promotion.IsActiveAt(now)`. Rien ne rapprochait les deux.
    ///
    /// Un acheteur ouvrant son paiement à 23 h 58 avec une promotion expirant à minuit,
    /// et appuyant sur « Payer » à 00 h 01, voyait 11 500 à l'écran et se faisait
    /// débiter 13 000 : la commande était figée au nouveau prix, et le prestataire
    /// réclamait ce montant-là. Aucun écran ne l'avertissait, et rien dans la requête ne
    /// portait le prix annoncé, donc rien ne pouvait s'en apercevoir.
    ///
    /// NUL = AUCUN CONTRÔLE, pour les appelants internes qui ne montrent rien à
    /// personne. Le BFF client, lui, l'EXIGE : c'est la seule surface où un humain lit un
    /// prix avant de payer, et c'est donc la seule qui puisse le promettre.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    decimal? ExpectedTotal = null) : ICommand<Guid>;

/// <summary>Adresse de livraison choisie au checkout (figée sur la commande).</summary>
public sealed record ShippingAddressInput(
    string? Label, string? Recipient, string? Phone,
    string? CommuneCode, string? Quartier, string? Landmark, string? Line1, string? CountryCode,
    double? Latitude, double? Longitude);
