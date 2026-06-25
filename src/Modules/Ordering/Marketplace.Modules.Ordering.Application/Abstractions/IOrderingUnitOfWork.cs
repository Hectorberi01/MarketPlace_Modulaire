using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Ordering.Application.Abstractions;

/// <summary>Unit of Work propre au module Ordering (évite la collision DI inter-modules).</summary>
public interface IOrderingUnitOfWork : IUnitOfWork
{
}
