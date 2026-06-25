using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.SetCommissionRate;

/// <summary>Définit le taux de commission négocié d'un vendeur (Admin).</summary>
public sealed record SetCommissionRateCommand(Guid SellerId, decimal CommissionRate) : ICommand;
