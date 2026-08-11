using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Domain.Deliveries;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUI EST TRANSPORTÉ — DÉCRIT, JAMAIS INTERPRÉTÉ.
///
/// La description est du TEXTE LIBRE, et c'est volontaire. Le moteur logistique
/// n'a pas à savoir qu'il s'agit d'un téléphone, d'un plat chaud ou d'un colis
/// partenaire : il a besoin de ce que le livreur doit lire pour choisir son
/// véhicule et son mode de transport.
///
/// Le poids et le caractère fragile ou périssable sont en revanche STRUCTURÉS,
/// parce qu'ils entrent dans des décisions automatiques : un colis de 40 kg
/// n'est pas proposé à une moto, un plat chaud passe avant un colis dans une
/// tournée. Ce sont des contraintes physiques, pas des catégories commerciales.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DeliveryPackage : ValueObject
{
    private const int MaxDescription = 300;

    /// <summary>
    /// Au-delà, aucune moto ne convient.
    ///
    /// ⚠️ Le nombre n'est plus écrit ici : il vient de <c>VehicleCapacity</c>, qui
    /// est désormais le seul endroit où les capacités de charge existent. Il était
    /// auparavant recopié à trois endroits — ici, dans la politique de dispatch, et
    /// dans les paliers des grilles tarifaires — sans que rien ne garantisse leur
    /// accord. C'est ce désaccord qui permettait de chiffrer un colis de 40 kg en
    /// moto puis de ne jamais trouver de livreur.
    /// </summary>
    public const decimal MotorcycleMaxWeightKg = Drivers.VehicleCapacity.MotorcycleMaxKg;

    private DeliveryPackage(string description, decimal? weightKg, bool isFragile, bool isPerishable)
    {
        Description = description;
        WeightKg = weightKg;
        IsFragile = isFragile;
        IsPerishable = isPerishable;
    }

    // Requis par EF Core.
    private DeliveryPackage()
    {
        Description = string.Empty;
    }

    /// <summary>Ce que le livreur lit. Texte libre, jamais interprété par le code.</summary>
    public string Description { get; private init; }

    /// <summary>Poids déclaré. Facultatif : personne ne pèse un repas.</summary>
    public decimal? WeightKg { get; private init; }

    public bool IsFragile { get; private init; }

    /// <summary>Périssable : impose un délai court, pas un type de véhicule.</summary>
    public bool IsPerishable { get; private init; }

    /// <summary>Le colis peut-il partir à moto ? Un poids inconnu est présumé transportable.</summary>
    public bool FitsOnMotorcycle => WeightKg is null || WeightKg <= MotorcycleMaxWeightKg;

    public static Result<DeliveryPackage> Create(
        string? description,
        decimal? weightKg = null,
        bool isFragile = false,
        bool isPerishable = false)
    {
        var trimmed = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmed is null)
        {
            return Result.Failure<DeliveryPackage>(
                Error.Validation("delivery.package.description_required", "Une description du colis est requise."));
        }

        if (weightKg is < 0)
        {
            return Result.Failure<DeliveryPackage>(
                Error.Validation("delivery.package.weight_negative", "Le poids ne peut pas être négatif."));
        }

        // Un poids nul déclaré n'est pas une erreur de saisie : c'est un champ
        // laissé à zéro par une intégration. On le traite comme « non renseigné »
        // plutôt que comme un colis sans poids, qui n'existe pas.
        var normalizedWeight = weightKg is null or 0m ? null : weightKg;

        return new DeliveryPackage(
            trimmed.Length <= MaxDescription ? trimmed : trimmed[..MaxDescription],
            normalizedWeight,
            isFragile,
            isPerishable);
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Description;
        yield return WeightKg;
        yield return IsFragile;
        yield return IsPerishable;
    }
}
