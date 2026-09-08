namespace HbaExpress.Dispatch.Domain.Couriers;

/// <summary>
/// Identité d'un coursier.
///
/// C'EST LE MÊME IDENTIFIANT QUE CELUI DE LA MARKETPLACE. Le module Delivery du
/// monolithe porte son identité de bénéficiaire (nom, versement, droit d'être payé) ;
/// ce service porte son identité opérationnelle (disponibilité, position, zone).
/// Deux moitiés d'une même personne, reliées par une seule clé — inventer un second
/// identifiant obligerait à tenir une table de correspondance, donc à la désynchroniser.
/// </summary>
public readonly record struct CourierId(Guid Value)
{
    public static CourierId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Droit de travailler, décidé par la MARKETPLACE et recopié ici par événement.
/// Le service ne l'invente pas : il l'apprend.
/// </summary>
public enum CourierStanding
{
    /// <summary>Pas encore vérifié : ne reçoit aucune offre.</summary>
    Pending = 0,
    Active = 1,
    Suspended = 2
}

/// <summary>
/// Disponibilité déclarée par le coursier lui-même, ici et maintenant.
///
/// Distincte du <see cref="CourierStanding"/> : « j'ai le droit de travailler » et
/// « je travaille en ce moment » sont deux questions différentes, et les confondre
/// enverrait des courses à quelqu'un qui a rangé sa moto.
/// </summary>
public enum CourierAvailability
{
    Offline = 0,
    Online = 1,

    /// <summary>En ligne mais déjà sur une course : ne reçoit plus d'offres.</summary>
    OnDelivery = 2
}

/// <summary>Type de véhicule — détermine ce que le coursier peut emporter.</summary>
public enum VehicleType
{
    Foot = 0,
    Bicycle = 1,
    Motorcycle = 2,
    Car = 3
}
