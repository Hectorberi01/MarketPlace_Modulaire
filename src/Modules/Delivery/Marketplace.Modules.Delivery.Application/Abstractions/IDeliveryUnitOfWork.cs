using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Delivery.Application.Abstractions;

/// <summary>Unit of Work propre au module Delivery (évite la collision DI inter-modules).</summary>
public interface IDeliveryUnitOfWork : IUnitOfWork
{
}
