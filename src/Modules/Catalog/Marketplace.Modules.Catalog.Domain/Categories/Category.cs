using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Categories.Events;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Domain.Categories;

/// <summary>Cycle de publication d'une catégorie dans l'arbre visible.</summary>
public enum CategoryStatus
{
    Draft = 0,
    Published = 1,
    Archived = 2
}

/// <summary>
/// Nœud de l'arbre des catégories. Porte le schéma d'attributs attendus pour ses
/// produits et peut avoir une image (cf. dossier, Category). Le chemin (Path)
/// est matérialisé pour des lectures d'arbre efficaces.
/// </summary>
public sealed class Category : AggregateRoot<CategoryId>
{
    private Category() { }

    private Category(
        CategoryId id,
        Guid? parentId,
        string name,
        Slug slug,
        string path,
        string? imageUrl,
        string attributeSchema)
        : base(id)
    {
        ParentId = parentId;
        Name = name;
        Slug = slug;
        Path = path;
        ImageUrl = imageUrl;
        AttributeSchema = attributeSchema;
        Status = CategoryStatus.Draft;

        Raise(new CategoryCreatedDomainEvent(id.Value, parentId, name, slug.Value, path));
    }

    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = default!;
    public Slug Slug { get; private set; } = default!;

    /// <summary>Chemin matérialisé, ex : « /electronique/telephones ».</summary>
    public string Path { get; private set; } = default!;

    public string? ImageUrl { get; private set; }

    /// <summary>
    /// Schéma d'attributs de CETTE catégorie, au format stocké (jsonb). Voir
    /// <see cref="CategoryAttributeSchema"/> pour le format et pour ce qu'il
    /// contraint. « {} » = aucune contrainte.
    ///
    /// La chaîne reste la forme persistée ; on n'expose pas un objet parsé en
    /// propriété EF, ce qui obligerait à un converter et à re-sérialiser à chaque
    /// écriture. Le parsing se fait à la demande, par <see cref="ParseSchema"/>.
    /// </summary>
    public string AttributeSchema { get; private set; } = "{}";

    /// <summary>
    /// Lit le schéma de cette catégorie.
    ///
    /// NE PEUT PAS ÉCHOUER EN PRATIQUE : `Create` et `Update` refusent un schéma
    /// illisible, donc ce qui est en base a déjà été validé. Le Result est conservé
    /// pour le cas d'une donnée écrite avant ce contrôle, ou à la main en base :
    /// mieux vaut alors une erreur de validation lisible qu'une exception au milieu
    /// d'une création de produit.
    /// </summary>
    public Result<CategoryAttributeSchema> ParseSchema() => CategoryAttributeSchema.Parse(AttributeSchema);

    public CategoryStatus Status { get; private set; }

    /// <summary>
    /// Construit le chemin matérialisé d'une catégorie à partir du chemin de son
    /// parent et de son slug.
    ///
    /// C'EST LE CHEMIN, ET NON LE SLUG, QUI IDENTIFIE UNE CATÉGORIE.
    ///
    /// Le slug ne porte que le nom : « alimentation » vaut pour les chiens comme pour
    /// les chats. L'imposer unique interdisait une taxonomie pourtant banale —
    /// impossible de créer « Animaux &gt; Chats &gt; Alimentation » dès lors que
    /// « Animaux &gt; Chiens &gt; Alimentation » existait.
    ///
    /// Le chemin, lui, porte la branche entière : « /animaux/chiens/alimentation »
    /// et « /animaux/chats/alimentation » diffèrent. Il reste unique là où il doit
    /// l'être — deux sœurs de même nom sous un même parent produisent le même chemin
    /// et sont donc toujours refusées.
    ///
    /// Exposé publiquement pour que l'Application vérifie la disponibilité d'un
    /// chemin AVANT de construire l'agrégat, sans réécrire cette règle de son côté.
    /// </summary>
    public static string BuildPath(string? parentPath, string slug)
    {
        var basePath = string.IsNullOrWhiteSpace(parentPath) ? string.Empty : parentPath.TrimEnd('/');
        return $"{basePath}/{slug}";
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LES CHEMINS DE TOUS LES ANCÊTRES, DÉDUITS DU SEUL CHEMIN MATÉRIALISÉ.
    ///
    /// « /informatique/ordinateurs/portables » donne « /informatique » puis
    /// « /informatique/ordinateurs », DANS CET ORDRE — de la racine au parent direct.
    /// L'ordre n'est pas cosmétique : c'est lui qui décide, à la composition des
    /// schémas, quel étage l'emporte sur une clé déclarée deux fois.
    ///
    /// POURQUOI ICI, ET POURQUOI PAS UNE REMONTÉE PAR `ParentId`. Remonter de parent en
    /// parent demande une requête par étage, donc autant d'allers-retours que la
    /// branche est profonde — et il faut les faire à chaque lecture de catégorie. Le
    /// chemin porte déjà la branche entière : une seule requête `WHERE path IN (…)`
    /// ramène tous les ancêtres, quelle que soit la profondeur.
    ///
    /// C'est le même raisonnement que <c>ListDescendantsAsync</c>, dans l'autre sens.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static IReadOnlyList<string> AncestorPathsOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Array.Empty<string>();
        }

        var segments = path.Trim().Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Un seul segment = catégorie racine : elle n'a pas d'ancêtre. La boucle
        // s'arrête à `segments.Length - 1` pour ne jamais rendre le chemin lui-même.
        var chemins = new List<string>(Math.Max(0, segments.Length - 1));
        var courant = string.Empty;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            courant = $"{courant}/{segments[i]}";
            chemins.Add(courant);
        }

        return chemins;
    }

    /// <summary>
    /// Crée une catégorie. <paramref name="parentPath"/> est le chemin du parent
    /// (résolu par l'Application en chargeant le parent), null si racine.
    /// </summary>
    public static Result<Category> Create(
        string name,
        Guid? parentId = null,
        string? parentPath = null,
        string? imageUrl = null,
        string? attributeSchema = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("catalog.category.name_required", "Le nom de la catégorie est obligatoire.");
        }

        var slugResult = Slug.Create(name);
        if (slugResult.IsFailure)
        {
            return Result.Failure<Category>(slugResult.Error);
        }

        // LE SCHÉMA EST VALIDÉ ICI, PAS À LA PREMIÈRE CRÉATION DE PRODUIT.
        //
        // Le champ n'était contrôlé nulle part : une saisie fautive traversait la
        // commande, atteignait une colonne `jsonb` et donnait un 500 opaque à
        // l'administrateur — ou pire, passait, et n'échouait qu'au moment où un
        // vendeur créait un produit dans cette catégorie, très loin de la faute.
        var schemaResult = CategoryAttributeSchema.Parse(attributeSchema);
        if (schemaResult.IsFailure)
        {
            return Result.Failure<Category>(schemaResult.Error);
        }

        var path = BuildPath(parentPath, slugResult.Value.Value);

        return new Category(
            CategoryId.New(),
            parentId == Guid.Empty ? null : parentId,
            name.Trim(),
            slugResult.Value,
            path,
            string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim(),
            string.IsNullOrWhiteSpace(attributeSchema) ? "{}" : attributeSchema.Trim());
    }

    /// <summary>Publie la catégorie (Draft -> Published), la rendant visible dans l'arbre.</summary>
    public Result Publish()
    {
        if (Status == CategoryStatus.Archived)
        {
            return Result.Failure(Error.Conflict("catalog.category.archived", "Une catégorie archivée ne peut pas être publiée."));
        }

        Status = CategoryStatus.Published;
        return Result.Success();
    }

    /// <summary>Dépublie la catégorie (Published -> Draft) ; elle pourra être republiée.</summary>
    public Result Unpublish()
    {
        if (Status == CategoryStatus.Archived)
        {
            return Result.Failure(Error.Conflict("catalog.category.archived", "Une catégorie archivée ne peut pas être dépubliée."));
        }

        Status = CategoryStatus.Draft;
        return Result.Success();
    }

    /// <summary>Archive la catégorie (la retire de l'arbre visible).</summary>
    public Result Archive()
    {
        Status = CategoryStatus.Archived;
        return Result.Success();
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// REPOSE LE CHEMIN D'UN DESCENDANT APRÈS LE RENOMMAGE D'UN ANCÊTRE.
    ///
    /// Le chemin matérialisé n'était PAS répercuté : renommer « Informatique »
    /// laissait ses enfants sur « /informatique/… » pendant que le parent passait à
    /// « /informatique-reseaux ». Tant que le chemin ne servait qu'à l'affichage,
    /// c'était un défaut cosmétique.
    ///
    /// L'HÉRITAGE EN FAIT UNE PERTE DE CONTRAINTE. Les ancêtres sont désormais
    /// retrouvés PAR LE CHEMIN (<see cref="AncestorPathsOf"/>) : une branche
    /// désaccordée ne remonte plus rien, la composition n'échoue pas — elle compose
    /// avec moins d'étages — et toutes les contraintes héritées disparaissent en
    /// silence, sur un simple renommage.
    ///
    /// ON REMPLACE UN PRÉFIXE, ON NE RECONSTRUIT PAS. Le descendant ne connaît ni son
    /// parent ni sa profondeur ; ce qui change est exactement le début de son chemin.
    /// Le contrôle d'appartenance au préfixe est ici plutôt que chez l'appelant : une
    /// catégorie qui n'est pas sous ce préfixe ne doit RIEN faire, et le lui demander
    /// est un défaut d'appel, pas une donnée à corriger en silence.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <param name="oldPrefix">Ancien chemin de l'ancêtre renommé.</param>
    /// <param name="newPrefix">Son nouveau chemin.</param>
    public Result RebasePath(string oldPrefix, string newPrefix)
    {
        // Le séparateur final délimite la branche : sans lui, « /animaux/chiens »
        // ramasserait « /animaux/chiens-de-chasse », qui n'en est pas un descendant.
        // Même raisonnement que ListDescendantsAsync, et il doit rester le même.
        var prefixe = oldPrefix.TrimEnd('/') + "/";
        if (!Path.StartsWith(prefixe, StringComparison.Ordinal))
        {
            return Result.Failure(Error.Validation(
                "catalog.category.not_under_prefix",
                $"La catégorie « {Name} » n'est pas sous « {oldPrefix} » : son chemin ne peut pas en dériver."));
        }

        Path = newPrefix.TrimEnd('/') + Path[oldPrefix.TrimEnd('/').Length..];
        return Result.Success();
    }

    /// <summary>
    /// Met à jour le nom (slug et chemin recalculés à partir du
    /// <paramref name="parentPath"/> fourni par l'Application), l'image et le
    /// schéma d'attributs. Le rattachement parent n'est pas modifié ici.
    ///
    /// LES CHEMINS DES DESCENDANTS SONT RÉPERCUTÉS PAR L'APPLICATION, qui seule les
    /// connaît — voir <see cref="RebasePath"/> et UpdateCategoryCommandHandler.
    /// </summary>
    public Result Update(string name, string? parentPath, string? imageUrl, string? attributeSchema)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("catalog.category.name_required", "Le nom de la catégorie est obligatoire."));
        }

        var slugResult = Slug.Create(name);
        if (slugResult.IsFailure)
        {
            return Result.Failure(slugResult.Error);
        }

        // Même contrôle qu'à la création : un schéma illisible est refusé au moment
        // où il est saisi, avec un message qui dit quoi corriger.
        var schemaResult = CategoryAttributeSchema.Parse(attributeSchema);
        if (schemaResult.IsFailure)
        {
            return Result.Failure(schemaResult.Error);
        }

        Name = name.Trim();
        Slug = slugResult.Value;
        Path = BuildPath(parentPath, slugResult.Value.Value);
        ImageUrl = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl.Trim();
        AttributeSchema = string.IsNullOrWhiteSpace(attributeSchema) ? "{}" : attributeSchema.Trim();

        return Result.Success();
    }
}
