namespace Marketplace.Modules.Identity.Domain.Users;

/// <summary>Identité forte d'un compte utilisateur. Source d'identité de tout le système.</summary>
public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
