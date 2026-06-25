using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Messaging.Application.Abstractions;

/// <summary>Unit of Work propre au module Messaging (évite la collision DI inter-modules).</summary>
public interface IMessagingUnitOfWork : IUnitOfWork
{
}
