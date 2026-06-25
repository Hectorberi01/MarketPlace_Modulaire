using Marketplace.BuildingBlocks.Domain.Events;

namespace Marketplace.Modules.Identity.Domain.Roles.Events;

public sealed record RoleCreatedDomainEvent(Guid RoleId, string Name) : DomainEvent;
