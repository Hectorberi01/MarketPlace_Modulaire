using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Loyalty.Application.Abstractions;

/// <summary>Unit of Work propre au module Loyalty (évite la collision DI inter-modules).</summary>
public interface ILoyaltyUnitOfWork : IUnitOfWork
{
}
