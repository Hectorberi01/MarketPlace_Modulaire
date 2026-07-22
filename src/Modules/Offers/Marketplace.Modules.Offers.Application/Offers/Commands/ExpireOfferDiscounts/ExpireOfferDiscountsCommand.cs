using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.ExpireOfferDiscounts;

/// <summary>Retire les remises échues (date de fin dépassée). Renvoie le nombre traité.</summary>
public sealed record ExpireOfferDiscountsCommand(int BatchSize = 100) : ICommand<int>;
