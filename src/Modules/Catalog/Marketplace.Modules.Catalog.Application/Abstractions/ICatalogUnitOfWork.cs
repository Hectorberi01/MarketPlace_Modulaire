using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Catalog.Application.Abstractions;

/// <summary>
/// Unit of Work propre au module Catalog. Une interface dédiée par module évite
/// la collision de résolution DI quand plusieurs DbContext (donc plusieurs
/// IUnitOfWork) coexistent dans le même conteneur.
/// </summary>
public interface ICatalogUnitOfWork : IUnitOfWork
{
}
