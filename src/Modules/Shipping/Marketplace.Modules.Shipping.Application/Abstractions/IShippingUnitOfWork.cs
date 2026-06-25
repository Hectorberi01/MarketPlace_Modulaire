using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Shipping.Application.Abstractions;

/// <summary>Unit of Work propre au module Shipping (évite la collision DI inter-modules).</summary>
public interface IShippingUnitOfWork : IUnitOfWork
{
}
