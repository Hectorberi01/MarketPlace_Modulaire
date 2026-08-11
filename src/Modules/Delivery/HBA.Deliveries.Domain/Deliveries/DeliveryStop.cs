using Marketplace.BuildingBlocks.Domain.Geography;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Domain.Deliveries;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN POINT DE LA COURSE — COLLECTE OU REMISE.
///
/// CE QUI EST OBLIGATOIRE, ET POURQUOI CE N'EST PAS LE GPS
///
/// Le cahier d'architecture décrit un point par ses coordonnées. Au Bénin, cela ne
/// suffit pas, et cela ne peut pas être la donnée principale :
///
///   • la plupart des rues n'ont ni nom ni numéro ; il n'existe pas de code postal
///     opérationnel — une adresse se donne par commune, quartier et point de repère ;
///   • un acheteur sur deux ne partagera pas sa position, par habitude ou par
///     économie de données ;
///   • un point GPS relevé dans un bâtiment dérive de plusieurs dizaines de mètres,
///     ce qui, dans un quartier dense, désigne la mauvaise concession.
///
/// Ce qu'un livreur utilise réellement, c'est le REPÈRE (« en face de la pharmacie
/// Sainte-Rita ») et le TÉLÉPHONE. Les deux sont donc obligatoires ; la position ne
/// l'est pas. Elle aide — elle ne remplace pas.
///
/// C'est l'inverse de ce que ferait une plateforme conçue pour l'Europe, et c'est
/// délibéré.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DeliveryStop : ValueObject
{
    private const int MaxContactName = 120;
    private const int MaxQuartier = 120;
    private const int MaxLandmark = 250;
    private const int MaxInstructions = 500;

    private DeliveryStop(
        string contactName,
        string phone,
        string communeCode,
        string? quartier,
        string landmark,
        string? instructions,
        Coordinates position)
    {
        ContactName = contactName;
        Phone = phone;
        CommuneCode = communeCode;
        Quartier = quartier;
        Landmark = landmark;
        Instructions = instructions;
        Position = position;
    }

    // Requis par EF Core.
    private DeliveryStop()
    {
        ContactName = string.Empty;
        Phone = string.Empty;
        CommuneCode = string.Empty;
        Landmark = string.Empty;
        Position = null!;
    }

    /// <summary>Personne à demander sur place.</summary>
    public string ContactName { get; private init; }

    /// <summary>Numéro joignable, au format national à 10 chiffres.</summary>
    public string Phone { get; private init; }

    /// <summary>Code de commune, issu du référentiel fermé des 77 communes.</summary>
    public string CommuneCode { get; private init; }

    /// <summary>Quartier, tel qu'il se dit. Facultatif.</summary>
    public string? Quartier { get; private init; }

    /// <summary>Point de repère. C'est l'information que le livreur lit en premier.</summary>
    public string Landmark { get; private init; }

    /// <summary>Consignes d'accès : étage, portail, « appeler avant ».</summary>
    public string? Instructions { get; private init; }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// POSITION OBLIGATOIRE — DÉCISION PRODUIT, PAS CONTRAINTE TECHNIQUE.
    ///
    /// Elle était facultative, et pour de bonnes raisons : au Bénin, les rues
    /// n'ont pas de nom, un acheteur sur deux ne partage pas sa position, et le
    /// point de repère reste ce qu'un livreur utilise réellement pour trouver la
    /// porte.
    ///
    /// Mais la tarification à la distance ne peut pas s'en passer : sans les deux
    /// extrémités, il n'y a ni kilomètres, ni zone, ni prix. Le choix est donc
    /// assumé — on exige la position pour pouvoir facturer au juste prix.
    ///
    /// ⚠️ LE REPÈRE RESTE OBLIGATOIRE, LUI AUSSI. La position sert à CALCULER ;
    /// le repère sert à TROUVER. Un point GPS relevé dans un bâtiment dérive de
    /// plusieurs dizaines de mètres — dans un quartier dense, cela désigne la
    /// mauvaise concession. Les deux sont complémentaires, jamais substituables.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public Coordinates Position { get; private init; }

    /// <summary>Libellé de la commune, résolu depuis le référentiel.</summary>
    public string CommuneName => BeninGeography.CommuneName(CommuneCode);

    /// <summary>Ce qu'on lit à voix haute à un livreur : le repère d'abord.</summary>
    public string Summary => string.Join(", ", new[] { Landmark, Quartier, CommuneName }
        .Where(s => !string.IsNullOrWhiteSpace(s)));

    public static Result<DeliveryStop> Create(
        string? contactName,
        string? phone,
        string? commune,
        string? quartier,
        string? landmark,
        string? instructions,
        Coordinates? position)
    {
        var name = Trim(contactName);
        if (name is null)
        {
            return Result.Failure<DeliveryStop>(
                Error.Validation("delivery.stop.contact_required", "Le nom du contact est requis."));
        }

        // Le téléphone est normalisé ET obligatoire. Un livreur qui ne trouve pas
        // appelle : sans numéro, le colis repart, et la course est perdue pour tout
        // le monde. C'est le champ le plus rentable du formulaire.
        var normalizedPhone = BeninGeography.NormalizePhone(phone);
        if (normalizedPhone is null)
        {
            return Result.Failure<DeliveryStop>(
                Error.Validation("delivery.stop.phone_invalid",
                    $"Un numéro joignable est requis ({BeninGeography.DialingCode} suivi de {BeninGeography.LocalPhoneLength} chiffres)."));
        }

        // La commune vient d'une liste FERMÉE : c'est le seul champ structuré de
        // l'adresse, donc le seul sur lequel on pourra un jour asseoir une zone
        // tarifaire ou une couverture de livraison.
        var communeCode = BeninGeography.ResolveCommuneCode(commune);
        if (communeCode is null)
        {
            return Result.Failure<DeliveryStop>(
                Error.Validation("delivery.stop.commune_unknown", "Commune inconnue. La livraison n'est possible qu'au Bénin."));
        }

        var mark = Trim(landmark);
        if (mark is null)
        {
            return Result.Failure<DeliveryStop>(
                Error.Validation("delivery.stop.landmark_required",
                    "Un point de repère est requis : c'est ce qui permet au livreur de trouver l'adresse."));
        }

        // La position conditionne le PRIX. Sans elle, aucune distance, donc aucun
        // devis — et une course sans montant n'est facturable par personne.
        if (position is null)
        {
            return Result.Failure<DeliveryStop>(
                Error.Validation("delivery.stop.position_required",
                    "La position est requise : elle sert à calculer la distance, donc le prix de la course."));
        }

        return new DeliveryStop(
            Cap(name, MaxContactName),
            normalizedPhone,
            communeCode,
            Clean(quartier, MaxQuartier),
            Cap(mark, MaxLandmark),
            Clean(instructions, MaxInstructions),
            position);
    }

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Cap(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static string? Clean(string? value, int max)
    {
        var trimmed = Trim(value);
        return trimmed is null ? null : Cap(trimmed, max);
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return ContactName;
        yield return Phone;
        yield return CommuneCode;
        yield return Quartier;
        yield return Landmark;
        yield return Instructions;
        yield return Position;
    }
}
