using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Tax.Application.Abstractions;

/// <summary>Unit of Work propre au module Tax (évite la collision DI inter-modules).</summary>
public interface ITaxUnitOfWork : IUnitOfWork
{
}
