using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Domain.Roles.Events;

namespace Marketplace.Modules.Identity.Domain.Roles;

/// <summary>
/// Définit ce qu'un acteur a le droit de faire : base de l'autorisation. Porte
/// un ensemble de permissions granulaires (cf. dossier, Role / Permission).
/// Les permissions sont stockées sous forme de codes validés par le VO Permission.
/// </summary>
public sealed class Role : AggregateRoot<RoleId>
{
    private List<string> _permissions = new();

    private Role()
    {
    }

    private Role(RoleId id, string name, string? description, bool isSystem, IEnumerable<string> permissions)
        : base(id)
    {
        Name = name;
        Description = description;
        IsSystem = isSystem;
        _permissions.AddRange(permissions);

        Raise(new RoleCreatedDomainEvent(id.Value, name));
    }

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }

    /// <summary>Rôle système (Buyer, Seller, Admin…) : non supprimable.</summary>
    public bool IsSystem { get; private set; }

    public IReadOnlyCollection<string> Permissions => _permissions.AsReadOnly();

    public static Result<Role> Create(
        string name,
        string? description = null,
        bool isSystem = false,
        IEnumerable<string>? permissions = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("identity.role.name_required", "Le nom du rôle est obligatoire.");
        }

        var codes = new List<string>();
        foreach (var permission in permissions ?? Enumerable.Empty<string>())
        {
            var result = Permission.Create(permission);
            if (result.IsFailure)
            {
                return Result.Failure<Role>(result.Error);
            }

            if (!codes.Contains(result.Value.Value))
            {
                codes.Add(result.Value.Value);
            }
        }

        return new Role(RoleId.New(), name.Trim(), description?.Trim(), isSystem, codes);
    }

    public Result AddPermission(string permission)
    {
        var result = Permission.Create(permission);
        if (result.IsFailure)
        {
            return Result.Failure(result.Error);
        }

        if (!_permissions.Contains(result.Value.Value))
        {
            _permissions.Add(result.Value.Value);
        }

        return Result.Success();
    }

    public Result RemovePermission(string permission)
    {
        _permissions.Remove(permission.Trim().ToLowerInvariant());
        return Result.Success();
    }

    /// <summary>Remplace l'intégralité des permissions du rôle.</summary>
    public Result SetPermissions(IEnumerable<string> permissions)
    {
        var codes = new List<string>();
        foreach (var permission in permissions)
        {
            var result = Permission.Create(permission);
            if (result.IsFailure)
            {
                return Result.Failure(result.Error);
            }

            if (!codes.Contains(result.Value.Value))
            {
                codes.Add(result.Value.Value);
            }
        }

        _permissions.Clear();
        _permissions.AddRange(codes);
        return Result.Success();
    }

    public Result Update(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("identity.role.name_required", "Le nom du rôle est obligatoire."));
        }

        Name = name.Trim();
        Description = description?.Trim();
        return Result.Success();
    }
}
