using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Offers.Application.Abstractions;

/// <summary>Unit of Work propre au module Offers (évite la collision DI inter-modules).</summary>
public interface IOffersUnitOfWork : IUnitOfWork
{
}
