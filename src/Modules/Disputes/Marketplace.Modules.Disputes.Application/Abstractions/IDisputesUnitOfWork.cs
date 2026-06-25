using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Disputes.Application.Abstractions;

/// <summary>Unit of Work propre au module Disputes (évite la collision DI inter-modules).</summary>
public interface IDisputesUnitOfWork : IUnitOfWork
{
}
