using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.UpdateSellerProfile;

/// <summary>Met à jour le nom de boutique, le logo et la description d'un vendeur.</summary>
public sealed record UpdateSellerProfileCommand(Guid SellerId, string ShopName, string? LogoUrl = null, string? Description = null) : ICommand;
