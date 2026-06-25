using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Notifications.Application.Abstractions;

/// <summary>Unit of Work propre au module Notifications (évite la collision DI inter-modules).</summary>
public interface INotificationsUnitOfWork : IUnitOfWork
{
}
