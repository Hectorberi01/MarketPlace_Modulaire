using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Identity.Application.Abstractions;

/// <summary>Unit of Work propre au module Identity (évite la collision DI inter-modules).</summary>
public interface IIdentityUnitOfWork : IUnitOfWork
{
}
