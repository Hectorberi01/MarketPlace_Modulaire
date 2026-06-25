using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Inventory.Domain.Locations;

/// <summary>
/// Adresse postale d'un lieu d'expédition, avec coordonnées géographiques
/// optionnelles (pour le calcul géolocalisé des frais de port). Value Object.
/// </summary>
public sealed class Address : ValueObject
{
    private Address(string line, string city, string country, double? latitude, double? longitude)
    {
        Line = line;
        City = city;
        Country = country;
        Latitude = latitude;
        Longitude = longitude;
    }

    public string Line { get; }
    public string City { get; }
    public string Country { get; }
    public double? Latitude { get; }
    public double? Longitude { get; }

    public static Result<Address> Create(string line, string city, string country, double? latitude = null, double? longitude = null)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return Error.Validation("inventory.address.line_required", "L'adresse est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            return Error.Validation("inventory.address.city_required", "La ville est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(country))
        {
            return Error.Validation("inventory.address.country_required", "Le pays est obligatoire.");
        }

        if (latitude is < -90 or > 90)
        {
            return Error.Validation("inventory.address.latitude_invalid", "Latitude invalide.");
        }

        if (longitude is < -180 or > 180)
        {
            return Error.Validation("inventory.address.longitude_invalid", "Longitude invalide.");
        }

        return new Address(line.Trim(), city.Trim(), country.Trim(), latitude, longitude);
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return Line;
        yield return City;
        yield return Country;
        yield return Latitude;
        yield return Longitude;
    }
}
