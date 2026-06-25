using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Search.Application.Abstractions;

/// <summary>Unit of Work propre au module Search (évite la collision DI inter-modules).</summary>
public interface ISearchUnitOfWork : IUnitOfWork
{
}
