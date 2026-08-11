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
        string countryCode, double? latitude, double? longitude, string? contactPhone)
    {
        CommuneCode = communeCode;
        Quartier = quartier;
        Landmark = landmark;
        Line = line;
        CountryCode = countryCode;
        Latitude = latitude;
        Longitude = longitude;
        ContactPhone = contactPhone;
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
    /// ═════════════════════════════════════════════════════════════════════════
    /// POSITION — OBLIGATOIRE À L'ÉCRITURE DEPUIS LA TARIFICATION À LA DISTANCE.
    ///
    /// Le commentaire d'origine annonçait « facultatives, pour un futur calcul de
    /// frais à la distance ». Ce futur est arrivé : c'est le point de COLLECTE
    /// d'une course, et sans lui il n'y a ni kilomètres, ni zone, ni prix. Une
    /// boutique sans position ne peut pas voir un seul de ses colis enlevé.
    ///
    /// Le type reste nullable pour les lignes ÉCRITES AVANT cette règle : EF les
    /// matérialise sans passer par <see cref="Create"/>, donc elles restent
    /// lisibles. Elles seront corrigées à la première modification du lieu.
    /// Aucun remplissage automatique — un centroïde de commune, c'est plusieurs
    /// kilomètres d'erreur dans le grand Cotonou.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public double? Latitude { get; }

    public double? Longitude { get; }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// NUMÉRO À APPELER SUR PLACE — CE CHAMP MANQUAIT ENTIÈREMENT.
    ///
    /// Le raccordement à HBA Delivery devait prendre le téléphone déclaré dans le
    /// dossier KYB du vendeur : un champ facultatif, celui du gérant, jamais conçu
    /// pour la logistique. Un vendeur avec trois boutiques donnait donc le même
    /// numéro pour les trois, et un livreur perdu devant la mauvaise porte
    /// appelait quelqu'un qui n'y était pas.
    ///
    /// Le numéro appartient au LIEU, pas à la personne morale. C'est le champ le
    /// plus rentable du formulaire : un livreur qui ne trouve pas appelle ; sans
    /// numéro, le colis repart et la course est perdue pour tout le monde.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    public string? ContactPhone { get; }

    /// <summary>Libellé de la commune, résolu à l'affichage.</summary>
    public string CommuneName => BeninGeography.CommuneName(CommuneCode);

    public static Result<Address> Create(
        string? commune, string? quartier, string? landmark, string? line,
        double? latitude = null, double? longitude = null, string? contactPhone = null)
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

        // La position conditionne l'enlèvement : sans elle, aucune distance, donc
        // aucun devis, donc aucun livreur. Voir l'encadré sur Latitude.
        if (latitude is null || longitude is null)
        {
            return Error.Validation(
                "inventory.address.position_required",
                "La position du lieu d'expédition est obligatoire : elle sert à calculer la distance de la "
                + "course, donc son prix. Sans elle, aucun livreur ne peut être envoyé chercher vos colis.");
        }

        // Le numéro est NORMALISÉ, pas seulement validé : on stocke une forme unique
        // (+229 suivi de 10 chiffres), quelle que soit celle saisie — même règle que
        // le carnet d'adresses acheteur, pour que les deux extrémités d'une course
        // soient comparables.
        var normalizedPhone = BeninGeography.NormalizePhone(contactPhone);
        if (normalizedPhone is null)
        {
            return Error.Validation(
                "inventory.address.contact_phone_required",
                $"Un numéro joignable sur place est obligatoire ({BeninGeography.DialingCode} suivi de "
                + $"{BeninGeography.LocalPhoneLength} chiffres) : c'est ce que le livreur compose quand il "
                + "ne trouve pas la boutique.");
        }

        // TRONQUER, pas laisser passer. `UpdateLocationAddressCommand` n'a pas de
        // validateur FluentValidation : sans cela, une rue de 600 caractères produit une
        // violation de contrainte en base, donc un 500 opaque, là où l'utilisateur
        // mériterait au pire une valeur coupée. Mêmes bornes que la configuration EF.
        return new Address(
            communeCode, Trim(quartier, 120), Truncate(cleanLandmark, 200), Trim(line, 500),
            BeninGeography.CountryCode, latitude, longitude, normalizedPhone);
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
        yield return ContactPhone;
    }
}
