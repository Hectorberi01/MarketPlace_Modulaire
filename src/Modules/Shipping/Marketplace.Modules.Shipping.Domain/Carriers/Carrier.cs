using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Shipping.Domain.Carriers;

/// <summary>Identité forte d'un transporteur.</summary>
public readonly record struct CarrierId(Guid Value)
{
    public static CarrierId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Transporteur du catalogue (géré par l'admin). Le vendeur en choisit un à
/// l'expédition au lieu de saisir un nom libre : meilleure qualité de données et,
/// surtout, un <b>lien de suivi cliquable</b> pour l'acheteur, dérivé du modèle
/// d'URL. Agrégat racine.
/// </summary>
public sealed class Carrier : AggregateRoot<CarrierId>
{
    private Carrier()
    {
    }

    private Carrier(CarrierId id, string name, string code, string? trackingUrlTemplate, string? logoUrl)
        : base(id)
    {
        Name = name;
        Code = code;
        TrackingUrlTemplate = trackingUrlTemplate;
        LogoUrl = logoUrl;
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public string Name { get; private set; } = default!;

    /// <summary>Code court UNIQUE (ex. « DHL », « CHRONO »), en majuscules.</summary>
    public string Code { get; private set; } = default!;

    /// <summary>
    /// Modèle d'URL de suivi contenant le jeton <c>{tracking}</c>, remplacé par le
    /// numéro de suivi. Null = pas de lien (le numéro reste affiché en clair).
    /// </summary>
    public string? TrackingUrlTemplate { get; private set; }

    public string? LogoUrl { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static Result<Carrier> Create(string name, string code, string? trackingUrlTemplate = null, string? logoUrl = null)
    {
        var nameError = ValidateName(name);
        if (nameError is not null)
        {
            return nameError;
        }

        var normalizedCode = NormalizeCode(code);
        var codeError = ValidateCode(normalizedCode);
        if (codeError is not null)
        {
            return codeError;
        }

        var templateError = ValidateTemplate(trackingUrlTemplate);
        if (templateError is not null)
        {
            return templateError;
        }

        return new Carrier(CarrierId.New(), name.Trim(), normalizedCode, Clean(trackingUrlTemplate), Clean(logoUrl));
    }

    /// <summary>Met à jour le libellé, le modèle de suivi et le logo. Le code est immuable.</summary>
    public Result Update(string name, string? trackingUrlTemplate, string? logoUrl)
    {
        var nameError = ValidateName(name);
        if (nameError is not null)
        {
            return Result.Failure(nameError) ;
        }

        var templateError = ValidateTemplate(trackingUrlTemplate);
        if (templateError is not null)
        {
            return Result.Failure(templateError);
        }

        Name = name.Trim();
        TrackingUrlTemplate = Clean(trackingUrlTemplate);
        LogoUrl = Clean(logoUrl);
        return Result.Success();
    }

    public Result SetActive(bool active)
    {
        IsActive = active;
        return Result.Success();
    }

    /// <summary>
    /// Construit l'URL de suivi pour un numéro donné (null si pas de modèle ou pas
    /// de numéro). Le domaine porte la règle : les deux fronts ne peuvent pas diverger.
    /// </summary>
    public string? BuildTrackingUrl(string? trackingNumber)
    {
        if (string.IsNullOrWhiteSpace(TrackingUrlTemplate) || string.IsNullOrWhiteSpace(trackingNumber))
        {
            return null;
        }

        return TrackingUrlTemplate.Replace("{tracking}", Uri.EscapeDataString(trackingNumber.Trim()));
    }

    private static string NormalizeCode(string code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    private static Error? ValidateName(string name)
        => string.IsNullOrWhiteSpace(name)
            ? Error.Validation("shipping.carrier.name_required", "Le nom du transporteur est obligatoire.")
            : name.Trim().Length > 100
                ? Error.Validation("shipping.carrier.name_too_long", "Le nom du transporteur ne peut pas dépasser 100 caractères.")
                : null;

    private static Error? ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Error.Validation("shipping.carrier.code_required", "Le code du transporteur est obligatoire.");
        }

        if (code.Length > 20)
        {
            return Error.Validation("shipping.carrier.code_too_long", "Le code ne peut pas dépasser 20 caractères.");
        }

        foreach (var c in code)
        {
            if (!char.IsLetterOrDigit(c) && c is not ('-' or '_'))
            {
                return Error.Validation("shipping.carrier.code_format", "Le code n'accepte que lettres, chiffres, tirets et underscores.");
            }
        }

        return null;
    }

    private static Error? ValidateTemplate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null; // Le modèle est facultatif.
        }

        if (!template.Contains("{tracking}", StringComparison.Ordinal))
        {
            return Error.Validation("shipping.carrier.template_placeholder", "Le modèle d'URL doit contenir le jeton {tracking}.");
        }

        if (template.Trim().Length > 300)
        {
            return Error.Validation("shipping.carrier.template_too_long", "Le modèle d'URL ne peut pas dépasser 300 caractères.");
        }

        return null;
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
