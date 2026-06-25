using Marketplace.BuildingBlocks.Application.Abstractions;

namespace Marketplace.Modules.Marketing.Application.Abstractions;

/// <summary>Unit of Work propre au module Marketing (évite la collision DI inter-modules).</summary>
public interface IMarketingUnitOfWork : IUnitOfWork
{
}
