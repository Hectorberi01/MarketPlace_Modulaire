namespace Marketplace.BuildingBlocks.Domain.Primitives;

/// <summary>
/// Value Object : objet sans identité, défini par ses valeurs (Money, Slug…).
/// Égalité structurelle via <see cref="GetAtomicValues"/>.
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    protected abstract IEnumerable<object?> GetAtomicValues();

    /// <summary>
    /// ⚠️ SANS CES DEUX OPÉRATEURS, `==` COMPARAIT LES RÉFÉRENCES.
    ///
    /// Surcharger `Equals` ne suffit pas : en C#, `a == b` sur un type référence
    /// n'appelle PAS `Equals` — il appelle `operator ==`, et à défaut, la comparaison
    /// de références. Deux objets-valeurs structurellement identiques étaient donc
    /// `Equals` mais jamais `==`.
    ///
    /// Ce n'était pas théorique. Dans `Product.AddVariant` :
    ///
    ///     if (_variants.Any(v => v.Sku == skuResult.Value))   // toujours FAUX
    ///         return Error.Conflict("catalog.variant.sku_duplicate", …);
    ///
    /// Le garde-fou ne s'est jamais déclenché : un même produit pouvait porter deux
    /// variantes de SKU identique. Or le SKU est la clé partagée avec Inventory et
    /// Pricing — un doublon rattache le stock et le prix à la mauvaise variante.
    ///
    /// La faute est silencieuse par construction : le code compile, se lit bien, et
    /// ment. C'est pourquoi la correction va ici, dans la classe de base, et non dans
    /// `AddVariant` : le même piège attend chaque `==` posé sur un Money, un Slug, un
    /// Email ou un PhoneNumber.
    /// </summary>
    public static bool operator ==(ValueObject? left, ValueObject? right)
        => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);

    public bool Equals(ValueObject? other)
        => other is not null && GetType() == other.GetType() && ValuesAreEqual(other);

    public override bool Equals(object? obj) => obj is ValueObject other && Equals(other);

    public override int GetHashCode()
        => GetAtomicValues().Aggregate(default(int), (hash, value) => HashCode.Combine(hash, value));

    private bool ValuesAreEqual(ValueObject other)
        => GetAtomicValues().SequenceEqual(other.GetAtomicValues());
}
