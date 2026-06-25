using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.SetPayoutAccount;

/// <summary>Définit les coordonnées de reversement d'un vendeur (MoMo, Wave, banque…).</summary>
public sealed record SetPayoutAccountCommand(Guid SellerId, string Provider, string AccountNumber, string AccountName) : ICommand;
