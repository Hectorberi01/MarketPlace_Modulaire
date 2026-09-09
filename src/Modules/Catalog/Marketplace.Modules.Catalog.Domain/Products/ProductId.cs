namespace Marketplace.Modules.Catalog.Domain.Products;

/// <summary>
/// Identité forte du produit (strongly-typed id). Évite de confondre un
/// ProductId avec un SellerId ou un CategoryId à la compilation.
/// </summary>
public readonly record struct ProductId(Guid Value) : IComparable<ProductId>
{
    public static ProductId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();

    // ═════════════════════════════════════════════════════════════════════════════
    // POURQUOI UN IDENTIFIANT EST COMPARABLE.
    //
    // Uniquement pour la PAGINATION PAR CURSEUR : parcourir tout le catalogue avec
    // un `Skip(n)` coûte de plus en plus cher à chaque page, et décale les pages
    // suivantes dès qu'une ligne est créée ou supprimée pendant le parcours — donc
    // saute des produits, en silence. Un curseur « après cet identifiant » ne saute
    // rien, quoi qu'il arrive entre deux pages.
    //
    // L'ORDRE N'A AUCUN SENS MÉTIER. Un identifiant plus « grand » n'est ni plus
    // récent ni plus important : c'est un ordre total arbitraire, et c'est
    // exactement ce qu'un curseur demande. Ne pas s'en servir pour trier un
    // affichage.
    //
    // La comparaison est celle de PostgreSQL, pas celle de .NET, dès lors qu'elle
    // est traduite en SQL — les deux n'ordonnent pas les UUID de la même façon.
    // Sans importance ici : le `ORDER BY` et le `WHERE` sont évalués par le même
    // moteur, donc ils s'accordent.
    // ═════════════════════════════════════════════════════════════════════════════
    public int CompareTo(ProductId other) => Value.CompareTo(other.Value);

    public static bool operator <(ProductId left, ProductId right) => left.CompareTo(right) < 0;
    public static bool operator >(ProductId left, ProductId right) => left.CompareTo(right) > 0;
    public static bool operator <=(ProductId left, ProductId right) => left.CompareTo(right) <= 0;
    public static bool operator >=(ProductId left, ProductId right) => left.CompareTo(right) >= 0;
}
