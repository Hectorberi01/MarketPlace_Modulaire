using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Reviews.Domain.Reviews;

/// <summary>Identité forte d'un avis.</summary>
public readonly record struct ReviewId(Guid Value)
{
    public static ReviewId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>Statut de modération d'un avis.</summary>
public enum ReviewStatus
{
    Published = 0,
    Flagged = 1,
    Rejected = 2
}

/// <summary>Note d'un avis : un entier de 1 à 5 étoiles. Value Object.</summary>
public sealed class Rating : ValueObject
{
    public const int Min = 1;
    public const int Max = 5;

    private Rating(int value) => Value = value;

    public int Value { get; }

    public static Result<Rating> Create(int value)
        => value is < Min or > Max
            ? Error.Validation("reviews.rating_invalid", "La note doit être comprise entre 1 et 5.")
            : new Rating(value);

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Value;
    }
}
