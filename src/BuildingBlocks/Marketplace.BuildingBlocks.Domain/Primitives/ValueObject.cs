namespace Marketplace.BuildingBlocks.Domain.Primitives;

/// <summary>
/// Value Object : objet sans identité, défini par ses valeurs (Money, Slug…).
/// Égalité structurelle via <see cref="GetAtomicValues"/>.
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetAtomicValues();

    public bool Equals(ValueObject? other)
        => other is not null && GetType() == other.GetType() && ValuesAreEqual(other);

    public override bool Equals(object? obj) => obj is ValueObject other && Equals(other);

    public override int GetHashCode()
        => GetAtomicValues().Aggregate(default(int), (hash, value) => HashCode.Combine(hash, value));

    private bool ValuesAreEqual(ValueObject other)
        => GetAtomicValues().SequenceEqual(other.GetAtomicValues());
}
