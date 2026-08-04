using Marketplace.BuildingBlocks.Domain.Geography;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Identity.Domain.Addresses;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// ADRESSE DE LIVRAISON — MODÈLE BÉNINOIS.
///
/// CE QU'UNE ADRESSE EST AU BÉNIN
///
/// Il n'y a pas de code postal en usage, et une grande partie des rues n'ont ni nom ni
/// numéro. On ne se repère pas à « 12 rue des Lilas » mais à « Cotonou, quartier
/// Fidjrossè, en face de la pharmacie Sainte-Rita ». Le modèle suit cette réalité plutôt
/// que le gabarit occidental :
///
///   • <see cref="CommuneCode"/> — OBLIGATOIRE, choisi parmi les 77 communes officielles.
///     C'est le seul champ structuré, donc le seul exploitable pour une zone de livraison.
///   • <see cref="Landmark"/> — OBLIGATOIRE. Le point de repère : c'est ce que le coursier
///     lit réellement.
///   • <see cref="Quartier"/> — facultatif, texte libre.
///   • <see cref="Line1"/> — FACULTATIF, et c'est l'inversion volontaire par rapport à
///     l'ancien modèle. La rue était obligatoire et le repère n'existait pas ; c'était
///     l'exact contraire de ce dont un livreur a besoin.
///   • <see cref="Recipient"/> et <see cref="Phone"/> — OBLIGATOIRES. La livraison se fait
///     par zem ou coursier, qui appelle avant d'arriver. Une adresse sans numéro n'est pas
///     une adresse, c'est un colis perdu.
///
/// POURQUOI CERTAINS CHAMPS SONT « string? » ALORS QU'ILS SONT OBLIGATOIRES
///
/// Ils le sont à L'ÉCRITURE, pas en lecture. Les adresses saisies avant cette refonte
/// n'ont ni commune normalisée ni repère, et il n'existe aucun moyen honnête de les
/// inventer. On ne les efface pas et on ne devine pas à leur place : elles restent
/// lisibles, <see cref="IsComplete"/> les signale, et le paiement les refuse tant que
/// l'acheteur ne les a pas complétées. Toute adresse créée ou modifiée depuis cette
/// refonte est complète — <see cref="Create"/> et <see cref="Update"/> l'imposent.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class Address : AggregateRoot<AddressId>
{
    public const int MaxLabel = 60;
    public const int MaxRecipient = 120;
    public const int MaxLine = 200;
    public const int MaxQuartier = 120;
    public const int MaxLandmark = 200;
    public const int MaxCommuneCode = 40;
    public const int MaxPhone = 20;

    private Address()
    {
    }

    private Address(AddressId id, Guid userId, bool isDefault)
        : base(id)
    {
        UserId = userId;
        IsDefault = isDefault;
        CountryCode = BeninGeography.CountryCode;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public Guid UserId { get; private set; }

    /// <summary>« Maison », « Bureau »… Confort de choix dans le carnet, sans effet métier.</summary>
    public string Label { get; private set; } = "Adresse";

    /// <summary>Nom de la personne à qui remettre le colis. Obligatoire à l'écriture.</summary>
    public string Recipient { get; private set; } = string.Empty;

    /// <summary>Numéro à appeler à l'arrivée, normalisé en <c>+229XXXXXXXXXX</c>. Obligatoire à l'écriture.</summary>
    public string Phone { get; private set; } = string.Empty;

    /// <summary>Code d'une des 77 communes (voir <see cref="BeninGeography"/>). Obligatoire à l'écriture.</summary>
    public string? CommuneCode { get; private set; }

    /// <summary>Quartier ou village. Texte libre : aucun référentiel national ne les recense.</summary>
    public string? Quartier { get; private set; }

    /// <summary>Point de repère (« en face de la pharmacie X »). Obligatoire à l'écriture.</summary>
    public string? Landmark { get; private set; }

    /// <summary>Rue, carré, numéro de maison — quand ils existent. Facultatif.</summary>
    public string? Line1 { get; private set; }

    /// <summary>ISO 3166-1 alpha-2. Vaut « BJ » ; la colonne existe pour une ouverture ultérieure.</summary>
    public string CountryCode { get; private set; } = BeninGeography.CountryCode;

    public bool IsDefault { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    /// <summary>Libellé de la commune, résolu à l'affichage. Jamais stocké : seul le code l'est.</summary>
    public string CommuneName => BeninGeography.CommuneName(CommuneCode);

    /// <summary>
    /// L'adresse est-elle livrable en l'état ?
    ///
    /// <c>false</c> pour les adresses antérieures à la refonte. Le checkout s'appuie
    /// dessus pour refuser le paiement, plutôt que d'envoyer un coursier chercher une
    /// maison sans repère et sans numéro à appeler.
    /// </summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Recipient)
        && BeninGeography.IsValidPhone(Phone)
        && BeninGeography.IsKnownCommune(CommuneCode)
        && !string.IsNullOrWhiteSpace(Landmark);

    public static Result<Address> Create(
        Guid userId, string? label, string? recipient, string? phone,
        string? communeCode, string? quartier, string? landmark, string? line1, bool isDefault)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure<Address>(Error.Validation("identity.address.user_required", "Utilisateur requis."));
        }

        var address = new Address(AddressId.New(), userId, isDefault);
        var applied = address.Apply(label, recipient, phone, communeCode, quartier, landmark, line1);

        return applied.IsFailure ? Result.Failure<Address>(applied.Error) : Result.Success(address);
    }

    /// <summary>
    /// Met à jour les champs modifiables. Applique EXACTEMENT les mêmes règles que
    /// <see cref="Create"/> : une adresse héritée incomplète devient complète dès sa
    /// première modification, ou elle est refusée. Le statut « par défaut » se pilote
    /// séparément (<see cref="MarkDefault"/> / <see cref="ClearDefault"/>).
    /// </summary>
    public Result Update(
        string? label, string? recipient, string? phone,
        string? communeCode, string? quartier, string? landmark, string? line1)
        => Apply(label, recipient, phone, communeCode, quartier, landmark, line1);

    public void MarkDefault() => IsDefault = true;

    public void ClearDefault() => IsDefault = false;

    private Result Apply(
        string? label, string? recipient, string? phone,
        string? communeCode, string? quartier, string? landmark, string? line1)
    {
        var cleanRecipient = Trim(recipient);
        if (cleanRecipient is null)
        {
            return Result.Failure(Error.Validation(
                "identity.address.recipient_required",
                "Le nom du destinataire est obligatoire : le livreur doit savoir à qui remettre le colis."));
        }

        // Le numéro est NORMALISÉ, pas seulement validé : on stocke une forme unique
        // (+229 suivi de 10 chiffres), quelle que soit celle saisie.
        var normalizedPhone = BeninGeography.NormalizePhone(phone);
        if (normalizedPhone is null)
        {
            return Result.Failure(Error.Validation(
                "identity.address.phone_invalid",
                $"Un numéro de téléphone béninois valide est obligatoire ({BeninGeography.DialingCode} suivi de {BeninGeography.LocalPhoneLength} chiffres)."));
        }

        // On accepte le code comme le libellé : les données reprises et les imports n'ont
        // que des libellés. Rien n'est inventé pour autant — une valeur inconnue est refusée.
        var resolvedCommune = BeninGeography.ResolveCommuneCode(communeCode);
        if (resolvedCommune is null)
        {
            return Result.Failure(Error.Validation(
                "identity.address.commune_required",
                "La commune est obligatoire et doit faire partie des 77 communes du Bénin."));
        }

        var cleanLandmark = Trim(landmark);
        if (cleanLandmark is null)
        {
            return Result.Failure(Error.Validation(
                "identity.address.landmark_required",
                "Le point de repère est obligatoire (ex. « en face de la pharmacie Sainte-Rita »)."));
        }

        Label = Cap(Trim(label) ?? "Adresse", MaxLabel);
        Recipient = Cap(cleanRecipient, MaxRecipient);
        Phone = normalizedPhone;
        CommuneCode = resolvedCommune;
        Quartier = Clean(quartier, MaxQuartier);
        Landmark = Cap(cleanLandmark, MaxLandmark);
        Line1 = Clean(line1, MaxLine);
        CountryCode = BeninGeography.CountryCode;

        return Result.Success();
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Borne une valeur DÉJÀ connue non nulle.
    ///
    /// ⚠️ Ne pas ajouter de surcharge <c>Cap(string?, int)</c> : en C#, deux méthodes qui
    /// ne diffèrent que par l'annotation de nullabilité ont la MÊME signature (CS0111).
    /// C'est l'erreur que cette paire de noms distincts évite.
    /// </summary>
    private static string Cap(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>Nettoie et borne : <c>null</c> si vide, tronqué si trop long.</summary>
    private static string? Clean(string? value, int max)
    {
        var trimmed = Trim(value);
        return trimmed is null ? null : Cap(trimmed, max);
    }
}
