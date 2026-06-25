using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Marketplace.Modules.Catalog.Domain.Products;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Marketplace.Modules.Catalog.Infrastructure.Persistence.Converters;

/// <summary>Convertit un dictionnaire d'attributs dynamiques en jsonb.</summary>
public sealed class AttributesJsonConverter : ValueConverter<Dictionary<string, string>, string>
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public AttributesJsonConverter()
        : base(
            value => JsonSerializer.Serialize(value, Options),
            json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, Options) ?? new Dictionary<string, string>())
    {
    }
}

/// <summary>Comparateur de valeur pour le suivi de modifications des attributs.</summary>
public sealed class AttributesJsonComparer : ValueComparer<Dictionary<string, string>>
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public AttributesJsonComparer()
        : base(
            (left, right) => JsonSerializer.Serialize(left, Options) == JsonSerializer.Serialize(right, Options),
            value => value == null ? 0 : JsonSerializer.Serialize(value, Options).GetHashCode(),
            value => JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(value, Options), Options) ?? new Dictionary<string, string>())
    {
    }
}

/// <summary>Convertit le Value Object Dimensions en jsonb (tableau [L,W,H]).</summary>
public sealed class DimensionsJsonConverter : ValueConverter<Dimensions, string>
{
    public DimensionsJsonConverter() : base(value => Serialize(value), json => Deserialize(json))
    {
    }

    private static string Serialize(Dimensions dimensions)
        => JsonSerializer.Serialize(new[] { dimensions.LengthMm, dimensions.WidthMm, dimensions.HeightMm });

    private static Dimensions Deserialize(string json)
    {
        var values = JsonSerializer.Deserialize<int[]>(json) ?? new[] { 0, 0, 0 };
        return Dimensions.Create(values[0], values[1], values[2]).Value;
    }
}
