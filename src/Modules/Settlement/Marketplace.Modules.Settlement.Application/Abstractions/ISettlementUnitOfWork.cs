using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Settlement.Application.Abstractions;

/// <summary>Unit of Work propre au module Settlement (évite la collision DI inter-modules).</summary>
public interface ISettlementUnitOfWork : IUnitOfWork
{
}
