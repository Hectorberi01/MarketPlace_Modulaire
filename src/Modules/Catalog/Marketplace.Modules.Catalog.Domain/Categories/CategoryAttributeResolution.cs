using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Catalog.Domain.Categories;

/// <summary>
/// Un attribut du schéma EFFECTIF d'une catégorie, avec la catégorie qui le définit.
/// </summary>
/// <param name="Definition">La définition elle-même, telle que son propriétaire l'a écrite.</param>
/// <param name="OwnerCategoryId">Catégorie où l'attribut est DÉCLARÉ.</param>
/// <param name="OwnerCategoryName">Nom de cette catégorie, pour l'afficher sans second appel.</param>
/// <param name="OwnerCategoryPath">Son chemin — sert à trier et à situer dans l'arbre.</param>
/// <param name="Inherited">
/// Faux quand la catégorie regardée est elle-même le propriétaire. C'est la SEULE
/// différence entre un attribut propre et un attribut hérité : la définition, elle,
/// est rigoureusement la même — il n'y a pas de copie.
/// </param>
public sealed record ResolvedCategoryAttribute(
    CategoryAttributeDefinition Definition,
    Guid OwnerCategoryId,
    string OwnerCategoryName,
    string OwnerCategoryPath,
    bool Inherited);

/// <summary>
/// Une clé déclarée à deux étages de la même branche. Ne devrait plus se produire —
/// l'écriture la refuse — mais les données antérieures à ce contrôle en portent.
/// </summary>
public sealed record CategoryAttributeConflict(
    string Key,
    Guid IgnoredCategoryId,
    string IgnoredCategoryName,
    string WinningCategoryName);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// LE SCHÉMA D'UNE CATÉGORIE, COMPOSÉ LE LONG DE SA BRANCHE.
///
/// Un produit rangé dans « Informatique › Ordinateurs › Ordinateurs portables » doit
/// respecter les attributs des trois étages. Ils ne sont RECOPIÉS NULLE PART : chaque
/// catégorie ne stocke que ce qu'elle déclare elle-même, et la composition se refait à
/// la lecture, à partir du chemin matérialisé.
///
/// C'EST CE QUI REND UN AJOUT DE PARENT RÉTROACTIF. Poser un attribut sur
/// « Informatique » le rend attendu, à l'instant même, dans toutes les feuilles
/// dessous. Une duplication physique aurait demandé de repasser sur chaque descendant
/// — et aurait laissé, le jour où ce passage échoue à mi-chemin, une branche où la
/// moitié des catégories connaissent une contrainte que l'autre moitié ignore.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// TROIS RÈGLES DE COMPOSITION, ET AUCUNE N'EST ARBITRAIRE.
///
/// 1. L'ORDRE VA DE LA RACINE À LA FEUILLE. Le vendeur lit d'abord le général
///    (« Marque », posé sur Informatique), puis le particulier (« Taille d'écran »,
///    posé sur Ordinateurs portables). C'est aussi l'ordre dans lequel un formulaire
///    se lit sans effort.
///
/// 2. EN CAS DE COLLISION DE CLÉ, L'ANCÊTRE GAGNE — et cela ne devrait jamais servir,
///    puisque déclarer une clé déjà portée par un parent est refusé à l'écriture. Les
///    données antérieures à ce contrôle, elles, peuvent en porter. Laisser l'enfant
///    gagner lui permettrait d'AFFAIBLIR la contrainte d'un parent (rendre facultatif
///    ce qui est obligatoire, rouvrir une liste fermée) : l'héritage ne voudrait plus
///    rien dire. Les collisions sont donc résolues en faveur du plus proche de la
///    racine, ET signalées dans <see cref="Conflicts"/> pour qu'on puisse les nettoyer.
///
/// 3. « ATTRIBUTS HORS SCHÉMA » : LE PLUS STRICT L'EMPORTE. Si un seul étage de la
///    branche refuse les clés inconnues, la branche entière les refuse. Un parent qui
///    ferme son jeu d'attributs ne peut pas être rouvert par un enfant — ce serait
///    contourner la règle en créant une sous-catégorie.
/// ─────────────────────────────────────────────────────────────────────────────────
///
/// OBJET PUREMENT CALCULATOIRE : il ne lit rien, il compose ce qu'on lui donne. Les
/// ancêtres sont chargés par l'Application, en une seule requête sur les chemins.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ResolvedCategorySchema
{
    private ResolvedCategorySchema(
        IReadOnlyList<ResolvedCategoryAttribute> all,
        IReadOnlyList<ResolvedCategoryAttribute> inherited,
        IReadOnlyList<ResolvedCategoryAttribute> own,
        IReadOnlyList<CategoryAttributeConflict> conflicts,
        bool allowUnknownAttributes,
        bool ownAllowUnknownAttributes,
        CategoryAttributeSchema effective)
    {
        All = all;
        Inherited = inherited;
        Own = own;
        Conflicts = conflicts;
        AllowUnknownAttributes = allowUnknownAttributes;
        OwnAllowUnknownAttributes = ownAllowUnknownAttributes;
        Effective = effective;
    }

    /// <summary>Tous les attributs attendus, de la racine à la feuille.</summary>
    public IReadOnlyList<ResolvedCategoryAttribute> All { get; }

    /// <summary>
    /// Ceux qui viennent d'un ancêtre. Lecture seule dans la catégorie regardée.
    ///
    /// PARTITIONNÉS UNE FOIS, À LA COMPOSITION. C'étaient des propriétés calculées, qui
    /// refaisaient un `Where(...).ToList()` à CHAQUE accès — trois parcours et trois
    /// allocations par catégorie, multipliés par les trois cents lignes d'un arbre
    /// projeté d'un coup.
    /// </summary>
    public IReadOnlyList<ResolvedCategoryAttribute> Inherited { get; }

    /// <summary>Ceux que la catégorie regardée déclare elle-même. Les seuls modifiables ici.</summary>
    public IReadOnlyList<ResolvedCategoryAttribute> Own { get; }

    /// <summary>Clés déclarées deux fois dans la branche — voir la règle 2.</summary>
    public IReadOnlyList<CategoryAttributeConflict> Conflicts { get; }

    /// <summary>Le plus strict de la branche (voir la règle 3).</summary>
    public bool AllowUnknownAttributes { get; }

    /// <summary>
    /// Ce que la catégorie regardée déclare POUR ELLE-MÊME, sans ses ancêtres.
    ///
    /// C'est cette valeur — et non la composée — que l'administration recharge dans son
    /// formulaire et réenvoie à chaque enregistrement. Lui servir la composée ferait
    /// écrire la contrainte d'un parent dans le schéma propre de l'enfant, où elle
    /// survivrait à sa levée chez le parent.
    /// </summary>
    public bool OwnAllowUnknownAttributes { get; }

    /// <summary>
    /// Le schéma composé, sous la forme qui sait valider et normaliser. C'est LUI que
    /// le contrôle des produits utilise — jamais le schéma d'un seul étage.
    /// </summary>
    public CategoryAttributeSchema Effective { get; }

    /// <summary>Vrai si la branche entière ne contraint rien.</summary>
    public bool IsEmpty => Effective.IsEmpty;

    /// <summary>
    /// Compose le schéma d'une catégorie avec ceux de ses ancêtres.
    /// </summary>
    /// <param name="ancestors">
    /// Les ancêtres, ORDONNÉS DE LA RACINE AU PARENT DIRECT. L'ordre n'est pas une
    /// commodité : c'est lui qui décide quel étage gagne une collision de clé.
    /// </param>
    /// <param name="category">La catégorie regardée. Ses attributs sont les « propres ».</param>
    /// <param name="schemaCache">
    /// Mémoire des schémas déjà lus, indexée par catégorie. Facultative.
    ///
    /// `ParseSchema` DÉSÉRIALISE du JSON à chaque appel, et une projection de l'arbre
    /// entier relit toute la branche de chaque catégorie : sur trois cents catégories
    /// de profondeur trois, plus de mille désérialisations pour un seul remplissage de
    /// cache. Le résultat ne dépend que de la catégorie — le mémoriser divise ce
    /// travail par la profondeur moyenne.
    ///
    /// Nulle pour un appel isolé : le dictionnaire ne vaut la peine que sur un lot.
    /// </param>
    public static Result<ResolvedCategorySchema> Compose(
        IReadOnlyList<Category> ancestors,
        Category category,
        Dictionary<Guid, Result<CategoryAttributeSchema>>? schemaCache = null)
    {
        var etages = new List<Category>(ancestors.Count + 1);
        etages.AddRange(ancestors);
        etages.Add(category);

        var attributs = new List<ResolvedCategoryAttribute>();
        var herites = new List<ResolvedCategoryAttribute>();
        var propres = new List<ResolvedCategoryAttribute>();
        var conflits = new List<CategoryAttributeConflict>();
        var vues = new Dictionary<string, ResolvedCategoryAttribute>(StringComparer.OrdinalIgnoreCase);
        var accepteInconnus = true;
        var accepteInconnusPropre = true;

        foreach (var etage in etages)
        {
            Result<CategoryAttributeSchema> lecture;
            if (schemaCache is not null && schemaCache.TryGetValue(etage.Id.Value, out var memorise))
            {
                lecture = memorise;
            }
            else
            {
                lecture = etage.ParseSchema();
                if (schemaCache is not null)
                {
                    schemaCache[etage.Id.Value] = lecture;
                }
            }

            if (lecture.IsFailure)
            {
                // UN SEUL ÉTAGE ILLISIBLE FAIT ÉCHOUER TOUTE LA BRANCHE, et le message
                // NOMME l'étage fautif. Composer en sautant l'étage donnerait un schéma
                // silencieusement amputé : les produits d'une feuille passeraient un
                // contrôle auquel il manque une contrainte, sans que rien ne le dise.
                return Error.Failure(
                    "catalog.category.ancestor_schema_unreadable",
                    $"Le schéma d'attributs de la catégorie « {etage.Name} » est illisible : "
                    + "tant qu'il n'est pas corrigé, aucun produit de cette branche ne peut être contrôlé.");
            }

            var schema = lecture.Value;

            // Le plus strict gagne : un seul « non » ferme la branche.
            accepteInconnus = accepteInconnus && schema.AllowUnknownAttributes;

            var estLaCategorie = ReferenceEquals(etage, category) || etage.Id == category.Id;
            if (estLaCategorie)
            {
                accepteInconnusPropre = schema.AllowUnknownAttributes;
            }

            foreach (var definition in schema.Attributes)
            {
                if (vues.TryGetValue(definition.Key, out var gagnant))
                {
                    conflits.Add(new CategoryAttributeConflict(
                        definition.Key, etage.Id.Value, etage.Name, gagnant.OwnerCategoryName));
                    continue;
                }

                var resolu = new ResolvedCategoryAttribute(
                    definition, etage.Id.Value, etage.Name, etage.Path, Inherited: !estLaCategorie);

                vues[definition.Key] = resolu;
                attributs.Add(resolu);
                (estLaCategorie ? propres : herites).Add(resolu);
            }
        }

        var effectif = CategoryAttributeSchema.FromDefinitions(
            attributs.Select(a => a.Definition).ToList(), accepteInconnus);

        return new ResolvedCategorySchema(
            attributs, herites, propres, conflits, accepteInconnus, accepteInconnusPropre, effectif);
    }
}
