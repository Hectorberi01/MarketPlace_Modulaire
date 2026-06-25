using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.Modules.Catalog.Contracts;

namespace Marketplace.Modules.Catalog.Application.Categories.Queries.GetCategory;

public sealed record GetCategoryQuery(Guid CategoryId) : IQuery<CategorySummary>;
