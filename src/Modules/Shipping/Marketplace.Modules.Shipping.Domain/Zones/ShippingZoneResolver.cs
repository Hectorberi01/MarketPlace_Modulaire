namespace Marketplace.Modules.Shipping.Domain.Zones;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// À QUELLE ZONE APPARTIENT UNE COMMUNE ?
///
/// Fonction PURE, sans dépendance ni accès base : c'est ce qui la rend testable, et
/// c'est là que vit la seule règle qui compte vraiment — celle qui décide combien
/// l'acheteur paie.
///
/// TROIS RÈGLES, DANS CET ORDRE
///
/// 1. La zone ACTIVE qui rattache explicitement cette commune gagne.
/// 2. À défaut, la zone de repli active.
/// 3. À défaut, rien — et l'appelant DOIT traiter ce cas.
///
/// LE CAS DE LA COMMUNE RATTACHÉE À DEUX ZONES
///
/// Rien en base ne l'interdit : deux administrateurs peuvent cocher Cotonou dans deux
/// zones différentes. Plutôt que de laisser l'ordre de lecture décider — donc le tarif
/// varier d'une requête à l'autre sans que personne ne comprenne — on tranche par un
/// critère STABLE : la zone la plus spécifique, c'est-à-dire celle qui couvre le MOINS
/// de communes. Une zone « Cotonou » à une commune l'emporte sur une zone « Sud » à
/// vingt. À nombre égal, le code alphabétique départage, faute de mieux, mais au moins
/// il départage toujours pareil.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class ShippingZoneResolver
{
    /// <summary>
    /// Zone applicable à une commune, ou <c>null</c> si aucune zone ne s'applique et
    /// qu'aucun repli actif n'existe.
    /// </summary>
    public static ShippingZone? Resolve(IEnumerable<ShippingZone> zones, string? communeCode)
    {
        var active = zones.Where(z => z.IsActive).ToList();

        var match = active
            .Where(z => !z.IsFallback && z.Covers(communeCode))
            .OrderBy(z => z.CommuneCodes.Count)
            .ThenBy(z => z.Code, StringComparer.Ordinal)
            .FirstOrDefault();

        if (match is not null)
        {
            return match;
        }

        // Un seul repli est censé exister ; s'il y en avait plusieurs (donnée abîmée),
        // on choisit toujours le même, pour que le tarif ne dépende pas du hasard.
        return active
            .Where(z => z.IsFallback)
            .OrderBy(z => z.Code, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// Communes qu'AUCUNE zone explicite ne couvre.
    ///
    /// Sert l'écran d'administration : elles fonctionnent — le repli les prend — mais au
    /// tarif le plus large. Les afficher évite qu'une commune reste au tarif national
    /// pendant des mois parce que personne n'a pensé à la rattacher.
    /// </summary>
    public static IReadOnlyList<string> UncoveredCommunes(
        IEnumerable<ShippingZone> zones,
        IEnumerable<string> allCommuneCodes)
    {
        var covered = zones
            .Where(z => z.IsActive && !z.IsFallback)
            .SelectMany(z => z.CommuneCodes)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return [.. allCommuneCodes.Where(c => !covered.Contains(c))];
    }
}
