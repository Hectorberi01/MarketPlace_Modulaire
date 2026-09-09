using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.LiftSellerSuspension;

/// <summary>
/// Lève la suspension d'un vendeur (Admin). Geste distinct de l'activation et de
/// l'approbation d'une réactivation : c'est la seule sortie de l'état suspendu, et
/// elle laisse trace du fait que la sanction a eu lieu.
/// </summary>
public sealed record LiftSellerSuspensionCommand(Guid SellerId) : ICommand;
