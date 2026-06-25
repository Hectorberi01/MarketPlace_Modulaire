using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Sellers.Application.Abstractions;

/// <summary>Unit of Work propre au module Sellers (évite la collision DI inter-modules).</summary>
public interface ISellerUnitOfWork : IUnitOfWork
{
}
