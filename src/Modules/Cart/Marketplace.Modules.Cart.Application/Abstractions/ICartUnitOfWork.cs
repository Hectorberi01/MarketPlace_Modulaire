using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Cart.Application.Abstractions;

/// <summary>Unit of Work propre au module Cart (évite la collision DI inter-modules).</summary>
public interface ICartUnitOfWork : IUnitOfWork
{
}
