using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.RegisterSeller;

/// <summary>Onboarde un vendeur rattaché à un compte Identity existant et vérifié.</summary>
public sealed record RegisterSellerCommand(Guid UserId, string ShopName, decimal CommissionRate = 0.10m) : ICommand<Guid>;
