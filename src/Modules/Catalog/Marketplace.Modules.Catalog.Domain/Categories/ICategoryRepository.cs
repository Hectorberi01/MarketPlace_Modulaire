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

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LES ANCÊTRES D'UNE CATÉGORIE, DE LA RACINE AU PARENT DIRECT, EN UNE REQUÊTE.
    ///
    /// C'est le pendant exact de <see cref="ListDescendantsAsync"/>, et il repose sur
    /// la même propriété du chemin matérialisé : les ancêtres de
    /// « /informatique/ordinateurs/portables » sont « /informatique » et
    /// « /informatique/ordinateurs » — des chemins que
    /// <see cref="Category.AncestorPathsOf"/> déduit sans rien lire.
    ///
    /// Une remontée par `ParentId` aurait coûté une requête par étage, à CHAQUE
    /// lecture de catégorie. Ici, un `WHERE path IN (…)` suffit quelle que soit la
    /// profondeur.
    ///
    /// L'ORDRE EST CONTRACTUEL, PAS INDICATIF : la composition des schémas s'en sert
    /// pour décider quel étage l'emporte sur une clé déclarée deux fois. Un
    /// `ORDER BY path` suffit — un chemin parent est toujours un préfixe du chemin
    /// enfant, donc plus court, donc classé avant.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    Task<IReadOnlyList<Category>> ListAncestorsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cette catégorie a-t-elle au moins un enfant DIRECT ?
    ///
    /// Sert à interdire la création d'un produit ailleurs que dans une feuille : ranger
    /// un ordinateur portable dans « Ordinateurs » plutôt que dans « Ordinateurs
    /// portables » le prive des attributs de la feuille, et le rend introuvable pour
    /// qui filtre sur eux.
    ///
    /// Enfant DIRECT et non descendant : les deux reviennent au même — on ne peut pas
    /// avoir un petit-enfant sans enfant — mais le test direct est le moins coûteux.
    /// </summary>
    Task<bool> HasChildrenAsync(Guid categoryId, CancellationToken cancellationToken = default);
}
