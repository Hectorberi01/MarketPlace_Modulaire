using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Reviews.Application.Abstractions;

/// <summary>Unit of Work propre au module Reviews (évite la collision DI inter-modules).</summary>
public interface IReviewsUnitOfWork : IUnitOfWork
{
}
