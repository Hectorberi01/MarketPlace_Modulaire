namespace Marketplace.Modules.Catalog.Domain.Categories;

public interface ICategoryRepository
{
    Task AddAsync(Category category, CancellationToken cancellationToken = default);

    void Remove(Category category);

    Task<Category?> GetByIdAsync(CategoryId id, CancellationToken cancellationToken = default);

    /// <summary>Liste toutes les catégories (sélecteur parent, gouvernance admin).</summary>
    Task<IReadOnlyList<Category>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Ce CHEMIN est-il déjà pris ? Remplace l'ancienne vérification d'unicité du
    /// slug, qui interdisait deux sous-catégories homonymes sous des parents
    /// différents — « Alimentation » sous « Chiens » ET sous « Chats », par exemple.
    ///
    /// Le chemin porte la branche entière : il reste unique entre sœurs tout en
    /// autorisant les homonymes ailleurs dans l'arbre.
    /// </summary>
    /// <param name="excludeId">
    /// Catégorie à ignorer — indispensable en modification, sans quoi une catégorie
    /// entrerait en conflit avec elle-même dès qu'on enregistre sans changer le nom.
    /// </param>
    Task<bool> PathExistsAsync(string path, Guid? excludeId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Descendants d'une catégorie, à TOUTE profondeur, ordonnés par chemin.
    ///
    /// S'appuie sur le chemin matérialisé : tout descendant de « /animaux/chiens »
    /// a un chemin commençant par « /animaux/chiens/ ». Une seule requête suffit
    /// donc, quelle que soit la profondeur — là où une descente niveau par niveau
    /// en exigerait autant que d'étages.
    ///
    /// Le séparateur final est essentiel : sans lui, « /animaux/chiens-de-chasse »
    /// serait ramassé comme un descendant de « /animaux/chiens ».
    /// </summary>
    Task<IReadOnlyList<Category>> ListDescendantsAsync(string path, CancellationToken cancellationToken = default);
}
