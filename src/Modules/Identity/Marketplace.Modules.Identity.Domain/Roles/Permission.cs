using System.Text.RegularExpressions;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Identity.Domain.Roles;

/// <summary>
/// Droit granulaire au format « ressource.action » (ex : catalog.write,
/// payout.read). Value Object porté par un rôle.
/// </summary>
public sealed partial class Permission : ValueObject
{
    private Permission(string value) => Value = value;

    public string Value { get; }

    public static Result<Permission> Create(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Error.Validation("identity.permission.empty", "La permission ne peut pas être vide.");
        }

        var normalized = input.Trim().ToLowerInvariant();

        if (normalized.Length > 100 || !PermissionPattern().IsMatch(normalized))
        {
            return Error.Validation("identity.permission.invalid", "Permission invalide (format « ressource.action »).");
        }

        return new Permission(normalized);
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z0-9_]+(\.[a-z0-9_]+)+$")]
    private static partial Regex PermissionPattern();
}
