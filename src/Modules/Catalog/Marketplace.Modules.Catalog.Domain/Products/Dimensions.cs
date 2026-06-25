using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Catalog.Domain.Products;

/// <summary>
/// Dimensions d'une variante en millimètres. Value Object utilisé par Shipping
/// pour le calcul des frais de port (cf. dossier, ProductVariant).
/// </summary>
public sealed class Dimensions : ValueObject
{
    private Dimensions(int lengthMm, int widthMm, int heightMm)
    {
        LengthMm = lengthMm;
        WidthMm = widthMm;
        HeightMm = heightMm;
    }

    public int LengthMm { get; }
    public int WidthMm { get; }
    public int HeightMm { get; }

    public static Result<Dimensions> Create(int lengthMm, int widthMm, int heightMm)
    {
        if (lengthMm < 0 || widthMm < 0 || heightMm < 0)
        {
            return Error.Validation("catalog.dimensions.negative", "Les dimensions ne peuvent pas être négatives.");
        }

        return new Dimensions(lengthMm, widthMm, heightMm);
    }

    protected override IEnumerable<object?> GetAtomicValues()
    {
        yield return LengthMm;
        yield return WidthMm;
        yield return HeightMm;
    }

    public override string ToString() => $"{LengthMm}x{WidthMm}x{HeightMm}mm";
}
