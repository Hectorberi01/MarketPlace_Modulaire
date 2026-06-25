using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.Modules.Sellers.Contracts;

namespace Marketplace.Modules.Sellers.Application.Sellers.Queries.GetSeller;

/// <summary>Récupère un vendeur par son identifiant.</summary>
public sealed record GetSellerQuery(Guid SellerId) : IQuery<SellerSummary>;
