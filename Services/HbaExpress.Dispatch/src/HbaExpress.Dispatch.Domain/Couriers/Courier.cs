using Marketplace.BuildingBlocks.Domain.Geography;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HbaExpress.Dispatch.Domain.Couriers;

/// <summary>
/// Le coursier vu par le DISPATCH : où il est, s'il travaille, ce qu'il peut porter.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// L'AUTRE MOITIÉ DU COURSIER.
///
/// Son identité de bénéficiaire — nom, téléphone, compte Mobile Money, droit d'être
/// payé — vit dans le module Delivery de la marketplace, avec son portefeuille. Ici
/// vit tout ce qui change à la minute : en ligne ou non, position, zone, véhicule.
///
/// LA SÉPARATION SUIT LE RYTHME DES DONNÉES, PAS LE MÉTIER. Une position se réécrit
/// toutes les quinze secondes ; un compte de versement change deux fois par an. Les
/// loger ensemble obligerait la base qui porte l'argent à encaisser le débit d'écriture
/// d'une flotte en mouvement.
///
/// <see cref="Standing"/> est RECOPIÉ depuis la marketplace par événement — le service
/// ne décide pas qui a le droit de travailler, il l'apprend. <see cref="Availability"/>,
/// au contraire, n'appartient qu'ici : c'est le coursier qui la déclare.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class Courier : AggregateRoot<CourierId>
{
    private readonly List<string> _communeCodes = new();

    private Courier()
    {
    }

    private Courier(CourierId id, string displayName, VehicleType vehicle)
        : base(id)
    {
        DisplayName = displayName;
        Vehicle = vehicle;
        Standing = CourierStanding.Pending;
        Availability = CourierAvailability.Offline;
        CreatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Nom d'affichage, recopié de la marketplace pour l'écran de l'acheteur.</summary>
    public string DisplayName { get; private set; } = default!;

    public VehicleType Vehicle { get; private set; }
    public CourierStanding Standing { get; private set; }
    public CourierAvailability Availability { get; private set; }

    /// <summary>Dernière position connue. Null tant qu'il n'a jamais émis de point.</summary>
    public GeoPoint? LastPosition { get; private set; }

    /// <summary>
    /// Horodatage de cette position. Une position sans date serait inutilisable : à
    /// quinze minutes, un coursier à moto est à dix kilomètres de là.
    /// </summary>
    public DateTime? LastPositionAtUtc { get; private set; }

    /// <summary>Communes desservies. Vide = aucune, donc aucune offre.</summary>
    public IReadOnlyList<string> CommuneCodes => _communeCodes.AsReadOnly();

    public DateTime CreatedAtUtc { get; private set; }

    public static Result<Courier> Register(Guid courierId, string displayName, VehicleType vehicle)
    {
        if (courierId == Guid.Empty)
        {
            return Error.Validation("courier.id_required", "L'identifiant du coursier est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return Error.Validation("courier.name_required", "Le nom du coursier est obligatoire.");
        }

        return new Courier(new CourierId(courierId), displayName.Trim(), vehicle);
    }

    /// <summary>Recopie le droit de travailler décidé par la marketplace.</summary>
    public void ApplyStanding(CourierStanding standing)
    {
        Standing = standing;

        // Un coursier suspendu est sorti de la file IMMÉDIATEMENT, sans attendre qu'il
        // se déconnecte. Le laisser « en ligne » le ferait continuer à recevoir des
        // offres jusqu'à sa prochaine action.
        if (standing != CourierStanding.Active)
        {
            Availability = CourierAvailability.Offline;
        }
    }

    /// <summary>Le coursier se déclare disponible. Refusé s'il n'a pas le droit de travailler.</summary>
    public Result GoOnline()
    {
        if (Standing != CourierStanding.Active)
        {
            return Result.Failure(Error.Validation(
                "courier.not_active", "Votre compte n'est pas encore validé ou a été suspendu."));
        }

        Availability = CourierAvailability.Online;
        return Result.Success();
    }

    public void GoOffline() => Availability = CourierAvailability.Offline;

    /// <summary>Le coursier a pris une course : il ne reçoit plus d'offres.</summary>
    public void MarkOnDelivery() => Availability = CourierAvailability.OnDelivery;

    /// <summary>Course terminée : il redevient disponible, s'il a toujours le droit.</summary>
    public void ReleaseFromDelivery()
        => Availability = Standing == CourierStanding.Active
            ? CourierAvailability.Online
            : CourierAvailability.Offline;

    public Result UpdatePosition(GeoPoint position, DateTime nowUtc)
    {
        LastPosition = position;
        LastPositionAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>
    /// Déclare les communes desservies. Chaque code est validé contre les 77 communes
    /// officielles : une faute de frappe ferait disparaître le coursier du dispatch sans
    /// que rien ne le signale.
    /// </summary>
    public Result SetServiceArea(IEnumerable<string>? communeCodes)
    {
        var resolved = new List<string>();
        foreach (var raw in communeCodes ?? Enumerable.Empty<string>())
        {
            var code = BeninGeography.ResolveCommuneCode(raw);
            if (code is null)
            {
                return Result.Failure(Error.Validation("courier.area.unknown_commune", $"Commune inconnue : « {raw} »."));
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

    public bool Serves(string? communeCode)
        => communeCode is not null && _communeCodes.Contains(communeCode);

    /// <summary>
    /// Ce coursier peut-il recevoir une offre EN CE MOMENT ?
    ///
    /// <paramref name="positionMaxAge"/> est ce qui distingue « en ligne » de
    /// « joignable » : une application fermée depuis dix minutes laisse un coursier
    /// marqué en ligne, avec une position périmée. Lui offrir une course, c'est perdre
    /// les vingt secondes du délai d'acceptation pour rien — et pendant ce temps
    /// l'acheteur attend.
    /// </summary>
    public bool CanReceiveOffers(DateTime nowUtc, TimeSpan positionMaxAge)
        => Standing == CourierStanding.Active
           && Availability == CourierAvailability.Online
           && LastPosition is not null
           && LastPositionAtUtc is { } at
           && nowUtc - at <= positionMaxAge;
}
