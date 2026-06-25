using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Billing.Application.Abstractions;

/// <summary>Unit of Work propre au module Billing (évite la collision DI inter-modules).</summary>
public interface IBillingUnitOfWork : IUnitOfWork
{
}
