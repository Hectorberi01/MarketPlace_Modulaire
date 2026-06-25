using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Inventory.Application.Abstractions;

/// <summary>Unit of Work propre au module Inventory (évite la collision DI inter-modules).</summary>
public interface IInventoryUnitOfWork : IUnitOfWork
{
}
