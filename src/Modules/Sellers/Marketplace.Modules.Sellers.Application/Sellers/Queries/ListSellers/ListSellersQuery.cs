using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.Modules.Sellers.Contracts;

namespace Marketplace.Modules.Sellers.Application.Sellers.Queries.ListSellers;

/// <summary>Liste les vendeurs (back-office d'administration).</summary>
public sealed record ListSellersQuery : IQuery<IReadOnlyList<SellerSummary>>;
