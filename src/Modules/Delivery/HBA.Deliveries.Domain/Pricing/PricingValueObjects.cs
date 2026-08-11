using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Domain.Pricing;

/// <summary>
/// Encombrement déclaré. Sert aux surcharges et au choix du véhicule, jamais à
/// deviner ce que contient le colis.
/// </summary>
public enum PackageSize
{
    /// <summary>Tient dans un sac à dos.</summary>
    Small = 0,

    /// <summary>Tient sur le porte-bagages d'une moto.</summary>
    Medium = 1,

    /// <summary>Exige un tricycle ou plus.</summary>
    Large = 2,

    /// <summary>Exige une camionnette.</summary>
    Bulky = 3
}

/// <summary>
/// Une distance en kilomètres.
///
/// ⚠️ ELLE EST TOUJOURS ROUTIÈRE, JAMAIS À VOL D'OISEAU. C'est la distinction la
/// plus coûteuse de tout le module tarifaire : entre les deux points de l'exemple
/// du cahier, l'orthodromie fait 3,4 km quand la route en fait 7,4. Facturer la
/// première reviendrait à perdre la moitié du prix sur chaque course.
/// </summary>
public sealed class Distance : ValueObject
{
    /// <summary>Plafond de garde-fou. Au-delà, c'est une erreur de saisie, pas une course.</summary>
    public const double MaxKilometers = 500;

    private Distance(double kilometers) => Kilometers = kilometers;

    // Requis par EF Core.
    private Distance()
    {
    }

    public double Kilometers { get; private init; }

    public static Result<Distance> FromKilometers(double kilometers)
    {
        if (double.IsNaN(kilometers) || double.IsInfinity(kilometers) || kilometers < 0)
        {
            return Result.Failure<Distance>(
                Error.Validation("pricing.distance_invalid", "Distance illisible."));
        }

        if (kilometers > MaxKilometers)
        {
            return Result.Failure<Distance>(
                Error.Validation("pricing.distance_too_large",
                    $"Distance de {kilometers:F0} km : au-delà de {MaxKilometers} km, il s'agit d'une erreur de saisie."));
        }

        // Arrondi au dixième : c'est la précision qu'un facteur routier approché
        // peut honnêtement prétendre. Afficher 7,4137 km donnerait l'illusion
        // d'une mesure.
        return new Distance(Math.Round(kilometers, 1));
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Kilometers;
    }

    public override string ToString() => $"{Kilometers:F1} km";
}

/// <summary>Un poids en kilogrammes.</summary>
public sealed class Weight : ValueObject
{
    /// <summary>Au-delà, aucun véhicule de la flotte ne convient.</summary>
    public const decimal MaxKilograms = 300m;

    private Weight(decimal kilograms) => Kilograms = kilograms;

    // Requis par EF Core.
    private Weight()
    {
    }

    public decimal Kilograms { get; private init; }

    public static Result<Weight> FromKilograms(decimal kilograms)
    {
        if (kilograms < 0)
        {
            return Result.Failure<Weight>(
                Error.Validation("pricing.weight_negative", "Le poids ne peut pas être négatif."));
        }

        if (kilograms > MaxKilograms)
        {
            return Result.Failure<Weight>(
                Error.Validation("pricing.weight_too_large",
                    $"Poids de {kilograms:F0} kg : au-delà de {MaxKilograms} kg, aucun véhicule de la flotte ne convient."));
        }

        return new Weight(decimal.Round(kilograms, 2));
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Kilograms;
    }

    public override string ToString() => $"{Kilograms:F2} kg";
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ARRONDI MONÉTAIRE — AU MULTIPLE DE 100 FCFA, COMPOSANT PAR COMPOSANT.
///
/// Deux règles étaient possibles : arrondir chaque ligne, ou seulement le total.
/// La seconde est mathématiquement plus juste, mais elle produit des devis dont
/// les lignes ne somment pas au total affiché — et chaque écart devient un
/// ticket de support qu'on ne peut ni confirmer ni infirmer.
///
/// Ici, un partenaire peut refaire le calcul à la main et retrouver exactement
/// notre chiffre. Sur une API vendue à des tiers, cette vérifiabilité vaut plus
/// que quelques francs de précision.
///
/// 100 FCFA : le plus petit pas qu'un prix de course affiche au Bénin.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class Rounding
{
    public const decimal Step = 100m;

    /// <summary>Arrondit au multiple de 100 supérieur. Un composant nul reste nul.</summary>
    public static decimal Up(decimal amount)
        => amount <= 0 ? 0m : Math.Ceiling(amount / Step) * Step;
}
