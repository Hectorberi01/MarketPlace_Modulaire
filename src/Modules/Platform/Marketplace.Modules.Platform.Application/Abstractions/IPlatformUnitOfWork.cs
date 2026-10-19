using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Platform.Application.Abstractions;

/// <summary>Unit of Work propre au module Platform (évite la collision DI inter-modules).</summary>
public interface IPlatformUnitOfWork : IUnitOfWork
{
}
