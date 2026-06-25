using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.Modules.Sellers.Contracts;

namespace Marketplace.Modules.Sellers.Application.Sellers.Queries.GetSellerByUser;

/// <summary>Récupère la boutique rattachée à un compte utilisateur.</summary>
public sealed record GetSellerByUserQuery(Guid UserId) : IQuery<SellerSummary>;
