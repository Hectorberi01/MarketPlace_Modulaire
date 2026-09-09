using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Reviews.Domain.Reviews;

/// <summary>Identité forte d'un avis.</summary>
public readonly record struct ReviewId(Guid Value)
{
    public static ReviewId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Statut de modération d'un avis.
///
/// SEUL `Rejected` RETIRE L'AVIS DES LECTURES PUBLIQUES. `Flagged` marque un avis
/// contesté qui attend la décision d'un modérateur — il reste affiché et continue de
/// compter dans les notes. Voir `ReviewRepository.PubliclyVisible` : c'est là que la
/// règle est appliquée, et là qu'est expliqué pourquoi `Flagged` a cessé de dépublier.
/// </summary>
public enum ReviewStatus
{
    /// <summary>Visible publiquement, compté dans les notes.</summary>
    Published = 0,

    /// <summary>Signalé par un utilisateur : visible, en attente de revue.</summary>
    Flagged = 1,

    /// <summary>Rejeté par un modérateur : retiré des lectures publiques et des notes.</summary>
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
