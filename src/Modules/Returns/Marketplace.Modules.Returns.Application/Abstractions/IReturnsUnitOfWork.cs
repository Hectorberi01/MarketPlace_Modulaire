using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Returns.Application.Abstractions;

/// <summary>Unit of Work propre au module Returns (évite la collision DI inter-modules).</summary>
public interface IReturnsUnitOfWork : IUnitOfWork
{
}
