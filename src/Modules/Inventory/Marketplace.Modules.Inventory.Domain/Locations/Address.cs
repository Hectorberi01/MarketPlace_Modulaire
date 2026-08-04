using Marketplace.BuildingBlocks.Domain.Geography;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Inventory.Domain.Locations;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// ADRESSE D'UN LIEU D'EXPÉDITION — MÊME MODÈLE QUE LE CARNET ACHETEUR.
///
/// Le coursier qui vient RETIRER un colis a exactement le même problème que celui qui le
/// LIVRE : sans commune normalisée ni point de repère, il ne trouve pas la boutique. Le
/// modèle est donc le même, volontairement — deux modèles d'adresse divergents dans une
/// même application, c'est deux fois les mêmes bogues.
///
/// CE QUE CETTE VERSION CORRIGE
///
/// Le pays était une chaîne libre, et les deux surfaces vendeur n'écrivaient pas la même
/// chose : « BJ » depuis l'application mobile, « Bénin » depuis la console web, dans la
/// MÊME colonne. C'est fini : <see cref="CountryCode"/> vaut « BJ », normalisé ici.
///
/// La commune est désormais un CODE issu des 77 communes officielles — donc exploitable
/// pour une tarification par zone, ce que « cotonou » / « Cotonou » / « COTONOU » ne
/// permettait pas.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class Address : ValueObject
{
    private Address(
        string communeCode, string? quartier, string? landmark, string? line,
        string countryCode, double? latitude, double? longitude)
    {
        CommuneCode = communeCode;
        Quartier = quartier;
        Landmark = landmark;
        Line = line;
        CountryCode = countryCode;
        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>Code d'une des 77 communes. Obligatoire.</summary>
    public string CommuneCode { get; }

    /// <summary>Quartier ou village. Texte libre, facultatif.</summary>
    public string? Quartier { get; }

    /// <summary>Point de repère. Obligatoire : c'est ce qui rend le lieu trouvable.</summary>
    public string? Landmark { get; }

    /// <summary>Rue, carré, numéro — quand ils existent. Facultatif.</summary>
    public string? Line { get; }

    /// <summary>ISO 3166-1 alpha-2, toujours « BJ » à ce jour.</summary>
    public string CountryCode { get; }

    /// <summary>
    /// Coordonnées facultatives, pour un futur calcul de frais à la distance.
    ///
    /// Elles sont RÉELLEMENT conservées : la requête de lecture les renvoyait en dur à
    /// « null », si bien qu'une saisie GPS était perdue entre l'écriture et la relecture.
    /// </summary>
    public double? Latitude { get; }

    public double? Longitude { get; }

    /// <summary>Libellé de la commune, résolu à l'affichage.</summary>
    public string CommuneName => BeninGeography.CommuneName(CommuneCode);

    public static Result<Address> Create(
        string? commune, string? quartier, string? landmark, string? line,
        double? latitude = null, double? longitude = null)
    {
        // Accepte le code comme le libellé : les données reprises n'ont que des libellés.
        var communeCode = BeninGeography.ResolveCommuneCode(commune);
        if (communeCode is null)
        {
            return Error.Validation(
                "inventory.address.commune_required",
                "La commune est obligatoire et doit faire partie des 77 communes du Bénin.");
        }

        var cleanLandmark = Trim(landmark);
        if (cleanLandmark is null)
        {
            return Error.Validation(
                "inventory.address.landmark_required",
                "Le point de repère est obligatoire : sans lui, le coursier ne trouve pas le lieu de retrait.");
        }

        if (latitude is < -90 or > 90)
        {
            return Error.Validation("inventory.address.latitude_invalid", "Latitude invalide.");
        }

        if (longitude is < -180 or > 180)
        {
            return Error.Validation("inventory.address.longitude_invalid", "Longitude invalide.");
        }

        // TRONQUER, pas laisser passer. `UpdateLocationAddressCommand` n'a pas de
        // validateur FluentValidation : sans cela, une rue de 600 caractères produit une
        // violation de contrainte en base, donc un 500 opaque, là où l'utilisateur
        // mériterait au pire une valeur coupée. Mêmes bornes que la configuration EF.
        return new Address(
            communeCode, Trim(quartier, 120), Truncate(cleanLandmark, 200), Trim(line, 500),
            BeninGeography.CountryCode, latitude, longitude);
    }

    private static string? Trim(string? value, int max)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return trimmed is null ? null : Truncate(trimmed, max);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return CommuneCode;
        yield return Quartier;
        yield return Landmark;
        yield return Line;
        yield return CountryCode;
        yield return Latitude;
        yield return Longitude;
    }
}
