using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Wishlist.Application.Abstractions;

/// <summary>Unit of Work propre au module Wishlist (évite la collision DI inter-modules).</summary>
public interface IWishlistUnitOfWork : IUnitOfWork
{
}
