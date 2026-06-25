namespace Marketplace.Modules.Identity.Domain.Roles;

/// <summary>Identité forte d'un rôle.</summary>
public readonly record struct RoleId(Guid Value)
{
    public static RoleId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
