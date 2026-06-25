namespace Marketplace.BuildingBlocks.Domain.Primitives;

/// <summary>
/// Entité : objet avec identité, comparé par son Id (pas par ses valeurs).
/// </summary>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    protected Entity(TId id) => Id = id;

    // Requis par EF Core (matérialisation).
    protected Entity()
    {
        Id = default!;
    }

    public TId Id { get; protected init; }

    public bool Equals(Entity<TId>? other)
        => other is not null && GetType() == other.GetType() && Id.Equals(other.Id);

    public override bool Equals(object? obj) => obj is Entity<TId> entity && Equals(entity);

    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}
