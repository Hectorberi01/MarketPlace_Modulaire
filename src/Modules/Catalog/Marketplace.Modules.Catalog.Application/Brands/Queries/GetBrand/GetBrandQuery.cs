using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.Modules.Catalog.Contracts;

namespace Marketplace.Modules.Catalog.Application.Brands.Queries.GetBrand;

public sealed record GetBrandQuery(Guid BrandId) : IQuery<BrandSummary>;
