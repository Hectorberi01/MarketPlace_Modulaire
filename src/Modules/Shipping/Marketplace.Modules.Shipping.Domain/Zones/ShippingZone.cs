using Marketplace.BuildingBlocks.Domain.Geography;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Shipping.Domain.Zones;

public readonly record struct ShippingZoneId(Guid Value)
{
    public static ShippingZoneId New() => new(Guid.NewGuid());
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// ZONE DE LIVRAISON — UN GROUPE DE COMMUNES ET SES TARIFS.
///
/// POURQUOI PAR COMMUNE ET PAS PAR DISTANCE
///
/// La commune est déjà saisie, obligatoire et normalisée sur l'adresse de livraison
/// comme sur le lieu d'expédition. Elle ne coûte rien de plus à collecter. Une
/// tarification à la distance exigerait des coordonnées que personne ne capture — ni
/// l'acheteur, ni le vendeur, ni aucun écran — et une précision dont un forfait n'a
/// pas besoin. « Cotonou → Cotonou » et « Cotonou → Natitingou » : la commune suffit
/// à séparer les deux, et c'est tout ce que la grille demande.
///
/// POURQUOI EN BASE ET PAS DANS LE CODE
///
/// Contrairement au découpage administratif — 77 communes figées depuis 1999 — une
/// grille tarifaire bouge : au gré du carburant, d'un partenariat coursier, d'une
/// opération commerciale. Elle doit se corriger un dimanche soir sans livraison de
/// code. D'où une table, et un écran d'administration.
///
/// LA ZONE DE REPLI
///
/// Exactement UNE zone porte <see cref="IsFallback"/>. Elle s'applique à toute commune
/// non rattachée — y compris une commune créée par un futur redécoupage administratif.
/// Sans elle, un acheteur d'une commune oubliée se verrait refuser sa commande sans
/// comprendre pourquoi ; avec elle, il paie le tarif le plus large et la livraison se
/// fait. Une grille incomplète doit dégrader le prix, jamais le service.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ShippingZone : AggregateRoot<ShippingZoneId>
{
    public const int MaxCode = 40;
    public const int MaxName = 120;

    private readonly List<string> _communeCodes = [];
    private readonly List<ShippingRate> _rates = [];

    private ShippingZone()
    {
    }

    private ShippingZone(ShippingZoneId id, string code, string name, bool isFallback)
        : base(id)
    {
        Code = code;
        Name = name;
        IsFallback = isFallback;
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Identifiant stable et lisible (« cotonou », « grand-nokoue », « national »).</summary>
    public string Code { get; private set; } = default!;

    /// <summary>Libellé montré à l'acheteur et à l'administrateur.</summary>
    public string Name { get; private set; } = default!;

    /// <summary>Zone appliquée aux communes non rattachées. Exactement une par plateforme.</summary>
    public bool IsFallback { get; private set; }

    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Codes de communes rattachés. Vide et légitime pour la zone de repli.</summary>
    public IReadOnlyList<string> CommuneCodes => _communeCodes.AsReadOnly();

    /// <summary>Tarifs par service (« standard », « express »).</summary>
    public IReadOnlyList<ShippingRate> Rates => _rates.AsReadOnly();

    public static Result<ShippingZone> Create(string? code, string? name, bool isFallback)
    {
        var cleanCode = Slugify(code);
        if (cleanCode is null)
        {
            return Result.Failure<ShippingZone>(Error.Validation(
                "shipping.zone.code_required", "Le code de la zone est obligatoire."));
        }

        var cleanName = Trim(name);
        if (cleanName is null)
        {
            return Result.Failure<ShippingZone>(Error.Validation(
                "shipping.zone.name_required", "Le nom de la zone est obligatoire."));
        }

        return Result.Success(new ShippingZone(ShippingZoneId.New(), cleanCode, Cap(cleanName, MaxName), isFallback));
    }

    public Result Rename(string? name)
    {
        var cleanName = Trim(name);
        if (cleanName is null)
        {
            return Result.Failure(Error.Validation(
                "shipping.zone.name_required", "Le nom de la zone est obligatoire."));
        }

        Name = Cap(cleanName, MaxName);
        return Result.Success();
    }

    /// <summary>
    /// Remplace INTÉGRALEMENT la liste des communes.
    ///
    /// Remplacement et non fusion : l'écran d'administration présente une liste à cocher,
    /// et décocher doit détacher. Chaque code est résolu contre <see cref="BeninGeography"/>
    /// — on n'accepte pas une commune qui n'existe pas, sinon la zone contiendrait une
    /// entrée qu'aucune adresse ne pourra jamais désigner.
    /// </summary>
    public Result SetCommunes(IEnumerable<string>? communeCodes)
    {
        var resolved = new List<string>();

        foreach (var raw in communeCodes ?? [])
        {
            var code = BeninGeography.ResolveCommuneCode(raw);
            if (code is null)
            {
                return Result.Failure(Error.Validation(
                    "shipping.zone.commune_unknown",
                    $"« {raw} » ne correspond à aucune des 77 communes du Bénin."));
            }

            if (!resolved.Contains(code))
            {
                resolved.Add(code);
            }
        }

        _communeCodes.Clear();
        _communeCodes.AddRange(resolved);
        return Result.Success();
    }

    /// <summary>
    /// Fixe le tarif d'un service. Crée la ligne si elle n'existe pas, la met à jour sinon.
    /// </summary>
    public Result SetRate(string? serviceCode, string? label, string? eta, decimal amount, string? currency)
    {
        var rate = ShippingRate.Create(serviceCode, label, eta, amount, currency);
        if (rate.IsFailure)
        {
            return Result.Failure(rate.Error);
        }

        var existing = _rates.FindIndex(r => r.ServiceCode == rate.Value.ServiceCode);
        if (existing >= 0)
        {
            _rates[existing] = rate.Value;
        }
        else
        {
            _rates.Add(rate.Value);
        }

        return Result.Success();
    }

    public Result RemoveRate(string? serviceCode)
    {
        var code = Slugify(serviceCode);
        var removed = code is not null && _rates.RemoveAll(r => r.ServiceCode == code) > 0;

        return removed
            ? Result.Success()
            : Result.Failure(Error.NotFound("shipping.zone.rate_not_found", "Tarif introuvable pour ce service."));
    }

    /// <summary>
    /// Désactive la zone.
    ///
    /// La zone de repli ne peut PAS être désactivée : c'est elle qui garantit qu'aucune
    /// commande ne reste sans tarif. La désactiver reviendrait à rendre la plateforme
    /// incapable de livrer une commune non rattachée, sans que rien ne le signale avant
    /// le premier acheteur concerné.
    /// </summary>
    public Result Deactivate()
    {
        if (IsFallback)
        {
            return Result.Failure(Error.Conflict(
                "shipping.zone.fallback_required",
                "La zone de repli ne peut pas être désactivée : elle couvre les communes non rattachées."));
        }

        IsActive = false;
        return Result.Success();
    }

    public void Activate() => IsActive = true;

    /// <summary>La zone couvre-t-elle cette commune ?</summary>
    public bool Covers(string? communeCode)
        => communeCode is not null
           && _communeCodes.Contains(communeCode, StringComparer.OrdinalIgnoreCase);

    // ── Interne ─────────────────────────────────────────────────────────────────

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Cap(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>
    /// Normalise un code : minuscules, tirets, sans accent. Les codes voyagent dans les
    /// URL et les corps JSON ; « Grand Nokoué » et « grand-nokoue » doivent désigner la
    /// même zone quel que soit l'écran qui les a saisis.
    /// </summary>
    private static string? Slugify(string? value)
    {
        var trimmed = Trim(value);
        if (trimmed is null)
        {
            return null;
        }

        var folded = BeninGeography.FoldForKey(trimmed).Replace(' ', '-');
        return folded.Length == 0 ? null : Cap(folded, MaxCode);
    }
}
