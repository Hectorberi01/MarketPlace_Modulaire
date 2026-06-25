using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Catalog.Domain.Brands.Events;

public sealed record BrandCreatedDomainEvent(Guid BrandId, string Name, string Slug) : DomainEvent;
