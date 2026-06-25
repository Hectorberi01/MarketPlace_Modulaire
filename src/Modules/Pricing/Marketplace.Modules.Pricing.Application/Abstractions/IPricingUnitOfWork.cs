using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Pricing.Application.Abstractions;

/// <summary>Unit of Work propre au module Pricing (évite la collision DI inter-modules).</summary>
public interface IPricingUnitOfWork : IUnitOfWork
{
}
