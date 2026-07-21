using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.DeleteSeller;

/// <summary>Suppression DÉFINITIVE d'un vendeur (admin). Purge aussi ses produits (Catalog).</summary>
public sealed record DeleteSellerCommand(Guid SellerId) : ICommand;
