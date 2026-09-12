using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Catalog.Domain.Products;

/// <summary>
/// Dimensions d'une variante, en millimètres.
///
/// <para>
/// ═════════════════════════════════════════════════════════════════════════════════
/// AUCUN LECTEUR. CE RÉSUMÉ EN ANNONÇAIT UN QUI N'A JAMAIS EXISTÉ.
///
/// Il disait « utilisé par Shipping pour le calcul des frais de port ». Le module
/// Shipping ne référence ce type nulle part, et il ne le fera pas tel qu'il est écrit :
/// <c>ShippingRate</c> pose que « le montant est un forfait, pas un calcul » et qu'il
/// « ne dépend ni du poids, ni du volume, ni du nombre d'articles ». Le seul point
/// d'entrée tarifaire prend une commune de destination.
///
/// Ces dimensions sont écrites, converties en jsonb, et jamais relues PAR UNE RÈGLE :
/// elles ne sont même pas projetées dans <c>ProductVariantSummary</c>, de sorte que le
/// vendeur qui les saisissait ne pouvait plus les consulter. Les deux seuls endroits
/// qui lisent leurs propriétés sont le convertisseur EF qui les sérialise
/// (<c>JsonConverters</c>) et un test unitaire — autrement dit, la donnée fait
/// l'aller-retour vers la base sans que personne ne la regarde.
///
/// Les deux consoles ont cessé de les demander : elles envoient les trois à
/// <c>null</c>, et <c>Product.AddVariant</c> ne construit un <c>Dimensions</c> que si
/// l'une des trois est renseignée. Plus aucun objet n'est donc créé en production.
///
/// Le type est conservé : il ne coûte rien, et il sera juste le jour où un tarif au
/// volume existera. Ce qui ne doit pas rester, c'est la phrase qui laisse croire qu'on
/// y est déjà — c'est elle que trouve celui qui se demande pourquoi ce champ existe.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </para>
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
