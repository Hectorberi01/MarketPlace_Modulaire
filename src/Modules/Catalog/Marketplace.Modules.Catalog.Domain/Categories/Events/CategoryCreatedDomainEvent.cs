using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Catalog.Domain.Categories.Events;

public sealed record CategoryCreatedDomainEvent(
    Guid CategoryId,
    Guid? ParentId,
    string Name,
    string Slug,
    string Path) : DomainEvent;
