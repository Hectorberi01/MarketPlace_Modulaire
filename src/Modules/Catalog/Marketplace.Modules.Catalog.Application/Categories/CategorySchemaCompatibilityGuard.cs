using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Categories;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Categories;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// UN SCHÉMA NE PEUT PAS SE DURCIR CONTRE DES PRODUITS QUI EXISTENT DÉJÀ.
///
/// C'est le contrôle qui manque dans la plupart des catalogues à attributs dynamiques,
/// et son absence produit toujours la même panne, à retardement : l'administration
/// retire un attribut, ou le rend obligatoire, ou raccourcit sa liste de valeurs — et
/// des centaines de fiches qui passaient hier deviennent INVALIDES. Pas invisibles,
/// pas signalées : invalides. Le vendeur ne s'en aperçoit qu'en ouvrant sa fiche pour
/// corriger un prix, et le refus qu'il reçoit parle d'un attribut qu'il n'a pas touché.
///
/// LA PORTÉE EST LA BRANCHE, PAS LA CATÉGORIE. Un attribut déclaré sur « Informatique »
/// est hérité par « Ordinateurs » et « Ordinateurs portables » : le durcir concerne
/// tous les produits rangés sous n'importe laquelle des trois.
///
/// ─────────────────────────────────────────────────────────────────────────────────
/// CE QUI EST REFUSÉ, ET POURQUOI CES CAS-LÀ.
///
/// Plutôt qu'une liste de transitions interdites — qu'on oublie d'étendre au premier
/// type d'attribut ajouté — le contrôle pose UNE question, la seule qui compte :
///
///     « Les produits qui existent aujourd'hui passeraient-ils encore ? »
///
/// Elle couvre mécaniquement tous les cas de durcissement, y compris ceux auxquels on
/// n'a pas pensé :
///
///   • RETRAIT d'un attribut que des produits portent → leur valeur deviendrait une
///     clé inconnue, refusée dès que la catégorie ferme son jeu d'attributs, et perdue
///     au premier enregistrement dans tous les cas.
///   • PASSAGE À OBLIGATOIRE alors que des produits ne le portent pas → ces fiches ne
///     peuvent plus être enregistrées du tout, même pour en changer le nom.
///   • RETRAIT D'UNE VALEUR d'une liste fermée qui est en usage.
///   • RESSERREMENT DES BORNES sous une valeur en usage, ou au-dessus.
///   • CHANGEMENT DE TYPE qui rend les valeurs existantes illisibles.
///
/// L'ÉLARGISSEMENT PASSE TOUJOURS : rendre facultatif, ajouter une valeur, desserrer
/// une borne, ajouter un attribut non obligatoire. Ce sont les gestes qu'on veut
/// encourager, et aucun ne casse une fiche existante.
///
/// LE MESSAGE DIT QUOI FAIRE. « Attribut utilisé » n'aide personne. On nomme l'attribut,
/// on donne le nombre de fiches concernées, et pour un passage à obligatoire on indique
/// le chemin en deux temps : le poser facultatif, laisser les vendeurs remplir, durcir
/// ensuite.
/// ─────────────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed class CategorySchemaCompatibilityGuard
{
    /// <summary>
    /// Au-delà, on cite un échantillon plutôt que la liste entière : un message
    /// d'erreur de trois cents valeurs n'est plus un message.
    /// </summary>
    private const int MaxValeursCitees = 5;

    private readonly ICategoryRepository _categories;
    private readonly IProductRepository _products;

    public CategorySchemaCompatibilityGuard(ICategoryRepository categories, IProductRepository products)
    {
        _categories = categories;
        _products = products;
    }

    /// <summary>
    /// Vérifie qu'un nouveau schéma reste compatible avec les produits de la branche.
    /// </summary>
    /// <param name="category">La catégorie dont le schéma change.</param>
    /// <param name="ancien">Son schéma actuel.</param>
    /// <param name="nouveau">Celui qu'on veut enregistrer.</param>
    public async Task<Result> EnsureCompatibleAsync(
        Category category,
        CategoryAttributeSchema ancien,
        CategoryAttributeSchema nouveau,
        CancellationToken cancellationToken)
    {
        // Rien ne change : pas une requête. C'est le cas de loin le plus fréquent —
        // l'administration renomme une catégorie ou change son image bien plus souvent
        // qu'elle ne touche au schéma.
        if (SontEquivalents(ancien, nouveau))
        {
            return Result.Success();
        }

        var portee = await PorteeAsync(category, cancellationToken);
        var fiches = await _products.ListAttributesByCategoriesAsync(portee, cancellationToken);

        // LA PORTÉE D'UNE RACINE N'EST PAS UN PÉRIMÈTRE BORNÉ, et le dire « borné à une
        // branche » était rassurant sur le mauvais axe : la branche d'« Informatique »,
        // c'est le rayon entier. On charge donc les dictionnaires d'attributs, pas les
        // agrégats — voir `ListAttributesByCategoriesAsync` — et le coût reste celui
        // d'une opération d'administration, rare et volontaire.
        if (fiches.Count == 0)
        {
            // Aucune fiche dans la branche : tout est permis, y compris le durcissement
            // le plus brutal. C'est exactement le moment où l'on veut pouvoir le faire.
            return Result.Success();
        }

        var problemes = new List<string>();
        var nouvellesParCle = nouveau.Attributes.ToDictionary(a => a.Key, StringComparer.OrdinalIgnoreCase);

        // ── 0. Fermer le jeu d'attributs ──────────────────────────────────────────
        //
        // C'EST LE DURCISSEMENT LE PLUS BRUTAL, ET IL PASSAIT À TRAVERS LES DEUX
        // BOUCLES CI-DESSOUS. Elles parcourent les attributs de l'ANCIEN et du NOUVEAU
        // schéma ; aucune ne regarde les clés que les fiches portent HORS schéma.
        // Passer `allowUnknown` de vrai à faux invalide pourtant d'un coup toute fiche
        // qui porte un attribut libre — et le commentaire de tête promettait de couvrir
        // « tous les cas de durcissement, y compris ceux auxquels on n'a pas pensé ».
        if (ancien.AllowUnknownAttributes && !nouveau.AllowUnknownAttributes)
        {
            var connues = nouveau.Attributes.Select(a => a.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var libres = fiches
                .SelectMany(f => f.Keys)
                .Where(k => !string.IsNullOrWhiteSpace(k) && !connues.Contains(k.Trim()))
                .Select(k => k.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (libres.Count > 0)
            {
                var echantillon = libres.Take(MaxValeursCitees).Select(k => $"« {k} »");
                var suite = libres.Count > MaxValeursCitees ? $" et {libres.Count - MaxValeursCitees} autre(s)" : string.Empty;

                problemes.Add(
                    $"Des fiches portent des caractéristiques hors schéma — {string.Join(", ", echantillon)}{suite} — "
                    + "que la fermeture du jeu d'attributs rendrait interdites. Déclarez-les au schéma, "
                    + "ou faites-les retirer des fiches avant de fermer.");
            }
        }

        // ── 1. Les attributs retirés ───────────────────────────────────────────────
        foreach (var retire in ancien.Attributes.Where(a => !nouvellesParCle.ContainsKey(a.Key)))
        {
            var utilisations = fiches.Count(f => LireValeur(f, retire.Key) is not null);
            if (utilisations > 0)
            {
                problemes.Add(
                    $"« {retire.Label} » est renseigné sur {Fiches(utilisations)} : le retirer effacerait "
                    + "cette donnée au prochain enregistrement de chaque fiche.");
            }
        }

        // ── 2. Les attributs conservés, mais durcis ────────────────────────────────
        foreach (var definition in nouveau.Attributes)
        {
            var etaitLa = ancien.Attributes.Any(a => string.Equals(a.Key, definition.Key, StringComparison.OrdinalIgnoreCase));

            if (definition.Required)
            {
                var sansValeur = fiches.Count(f => LireValeur(f, definition.Key) is null);
                if (sansValeur > 0)
                {
                    problemes.Add(
                        $"« {definition.Label} » deviendrait obligatoire alors que {Fiches(sansValeur)} "
                        + (sansValeur == 1 ? "ne le porte pas" : "ne le portent pas")
                        + " : ces fiches ne pourraient plus être enregistrées du tout, même pour en corriger "
                        + "le prix. Laissez-le facultatif le temps que les vendeurs le renseignent, puis "
                        + "rendez-le obligatoire.");
                    // On ne cumule pas avec le contrôle de valeurs ci-dessous pour le
                    // même attribut : deux reproches sur une seule ligne se lisent mal.
                    continue;
                }
            }

            // Un attribut NOUVEAU et facultatif ne peut rien casser : aucune fiche ne le
            // porte, et rien ne les oblige à le porter.
            if (!etaitLa)
            {
                continue;
            }

            var refusees = fiches
                .Select(f => LireValeur(f, definition.Key))
                .Where(v => v is not null)
                .Select(v => v!)
                .Where(v => !ValeurAcceptee(definition, v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (refusees.Count > 0)
            {
                var echantillon = refusees.Take(MaxValeursCitees).Select(v => $"« {v} »");
                var suite = refusees.Count > MaxValeursCitees
                    ? $" et {refusees.Count - MaxValeursCitees} autre(s)"
                    : string.Empty;

                problemes.Add(
                    $"« {definition.Label} » : {string.Join(", ", echantillon)}{suite} "
                    + (refusees.Count == 1 ? "est une valeur en usage" : "sont des valeurs en usage")
                    + " que la nouvelle définition refuse.");
            }
        }

        return problemes.Count == 0
            ? Result.Success()
            : Result.Failure(Error.Conflict(
                "catalog.category.schema_breaks_products",
                "Ce schéma casserait des fiches existantes. " + string.Join(" ", problemes)));
    }

    /// <summary>
    /// La catégorie et TOUS ses descendants : l'attribut y est hérité, donc ses produits
    /// sont concernés au même titre que ceux de la catégorie elle-même.
    /// </summary>
    private async Task<IReadOnlyCollection<Guid>> PorteeAsync(Category category, CancellationToken ct)
    {
        var descendants = await _categories.ListDescendantsAsync(category.Path, ct);
        var ids = new List<Guid>(descendants.Count + 1) { category.Id.Value };
        ids.AddRange(descendants.Select(d => d.Id.Value));
        return ids;
    }

    /// <summary>
    /// Valeur d'une clé dans les attributs d'un produit, ou nul si absente ou vide.
    ///
    /// Recherche INSENSIBLE À LA CASSE, comme `CategoryAttributeSchema.Validate` : une
    /// fiche enregistrée avant la normalisation des clés peut porter « Couleur » là où
    /// le schéma dit « couleur ». La traiter comme absente ferait conclure à tort que
    /// l'attribut n'est pas utilisé — et autoriserait un retrait qui perd la donnée.
    ///
    /// Le blanc vaut l'absence, toujours pour la même raison : `Validate` considère
    /// qu'une chaîne vide ne satisfait pas un attribut obligatoire.
    /// </summary>
    private static string? LireValeur(IReadOnlyDictionary<string, string> attributs, string cle)
    {
        if (attributs.TryGetValue(cle, out var directe))
        {
            return string.IsNullOrWhiteSpace(directe) ? null : directe.Trim();
        }

        foreach (var paire in attributs)
        {
            if (string.Equals(paire.Key, cle, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(paire.Value) ? null : paire.Value.Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// Cette valeur passerait-elle la nouvelle définition ?
    ///
    /// On réutilise le schéma lui-même plutôt que de recopier ses règles : un schéma à
    /// un seul attribut, et sa propre validation. Le jour où un type est ajouté, ce
    /// contrôle le connaît sans qu'on y touche — c'est tout l'intérêt de ne pas
    /// réécrire la règle ici.
    /// </summary>
    private static bool ValeurAcceptee(CategoryAttributeDefinition definition, string valeur)
    {
        var seul = CategoryAttributeSchema.FromDefinitions(new[] { definition }, allowUnknown: true);
        return seul.Validate(new Dictionary<string, string> { [definition.Key] = valeur }).IsSuccess;
    }

    /// <summary>
    /// Les deux schémas contraignent-ils exactement pareil ?
    ///
    /// Comparaison structurelle et non textuelle : l'administration réenregistre le
    /// même schéma à chaque sauvegarde de catégorie — un simple renommage renvoie le
    /// JSON entier — et comparer les chaînes déclencherait le balayage des produits sur
    /// une différence d'espaces ou d'ordre de propriétés.
    /// </summary>
    private static bool SontEquivalents(CategoryAttributeSchema a, CategoryAttributeSchema b)
    {
        if (a.AllowUnknownAttributes != b.AllowUnknownAttributes) return false;
        if (a.Attributes.Count != b.Attributes.Count) return false;

        var parCle = b.Attributes.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var gauche in a.Attributes)
        {
            if (!parCle.TryGetValue(gauche.Key, out var droite)) return false;
            if (gauche.Type != droite.Type) return false;
            if (gauche.Required != droite.Required) return false;
            if (gauche.Min != droite.Min || gauche.Max != droite.Max) return false;
            if (!gauche.Values.SequenceEqual(droite.Values, StringComparer.Ordinal)) return false;
        }

        return true;
    }

    private static string Fiches(int nombre)
        => nombre == 1 ? "1 fiche produit" : $"{nombre} fiches produit";
}
