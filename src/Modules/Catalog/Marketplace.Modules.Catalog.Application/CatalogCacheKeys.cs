namespace Marketplace.Modules.Catalog.Application;

/// <summary>
/// Clés de cache du catalogue (cache-aside via ICacheService / Redis).
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POURQUOI CE FICHIER EXISTE
///
/// Le catalogue représente l'essentiel du trafic d'une marketplace : accueil,
/// catégories, recherche, fiches produit. Jusqu'ici, chaque affichage repartait
/// interroger la base pour recalculer une réponse IDENTIQUE pour tout le monde.
///
/// Les clés sont centralisées ici, et nulle part ailleurs, pour une raison très
/// concrète : une clé écrite à la main dans un handler de lecture et une autre,
/// légèrement différente, dans un handler d'écriture, produisent un cache qui ne
/// s'invalide JAMAIS. Le bug est invisible en développement (cache vide) et
/// permanent en production. Une faute de frappe suffit.
///
/// Toute nouvelle clé passe par ici.
/// ─────────────────────────────────────────────────────────────────────────────
///
/// <para>
/// Les DURÉES ci-dessous ne sont pas arbitraires ; elles répondent à la question
/// « combien de temps un utilisateur peut-il voir une donnée périmée sans que ce
/// soit un problème ? » — et la réponse n'est pas la même pour un nom de catégorie
/// et pour un prix.
/// </para>
///
/// <para>
/// À noter : les PRIX ne sont pas dans ce module. Ils vivent dans Offers, avec un
/// TTL beaucoup plus court. Un nom de produit périmé de cinq minutes n'ennuie
/// personne ; un prix périmé de cinq minutes est une promesse commerciale que l'on
/// ne tient pas.
/// </para>
public static class CatalogCacheKeys
{
    /// <summary>
    /// Fiche produit. Chemin le plus chaud de toute l'application (et anonyme).
    /// Invalidée explicitement à chaque écriture sur le produit.
    /// </summary>
    public static string Product(Guid productId) => $"catalog:product:{productId}";

    /// <summary>Produits d'une boutique (back-office vendeur, et page boutique).</summary>
    public static string ProductsBySeller(Guid sellerId) => $"catalog:products:seller:{sellerId}";

    /// <summary>Catégorie unitaire.</summary>
    public static string Category(Guid categoryId) => $"catalog:category:{categoryId}";

    /// <summary>Arbre des catégories. Lu à chaque ouverture de l'application.</summary>
    public const string AllCategories = "catalog:categories:all";

    /// <summary>Marque unitaire.</summary>
    public static string Brand(Guid brandId) => $"catalog:brand:{brandId}";

    /// <summary>Toutes les marques.</summary>
    public const string AllBrands = "catalog:brands:all";

    /// <summary>
    /// Produit : 5 minutes.
    ///
    /// Le contenu (nom, description, photos, déclinaisons) change rarement, et une
    /// écriture invalide la clé immédiatement. Ce TTL n'est donc qu'un filet de
    /// sécurité : il borne la casse si une invalidation est manquée — un cache mal
    /// invalidé qui n'expire jamais est un bug éternel.
    /// </summary>
    public static readonly TimeSpan ProductTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Catégories et marques : 30 minutes.
    ///
    /// Ce sont des données de référence : quelques dizaines de lignes, modifiées
    /// quelques fois par an, lues à CHAQUE ouverture de l'application. Le rapport
    /// lecture/écriture y est de plusieurs millions pour un. C'est le cache le plus
    /// rentable du système, et de loin.
    /// </summary>
    public static readonly TimeSpan ReferenceDataTtl = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Liste des produits d'une boutique : 2 minutes.
    ///
    /// Plus court, parce qu'un vendeur qui vient de publier un produit doit le voir
    /// apparaître dans SA liste. L'invalidation à l'écriture s'en charge déjà ; ce
    /// TTL borne le pire cas.
    /// </summary>
    public static readonly TimeSpan SellerProductsTtl = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Durée de mémorisation d'une ABSENCE (produit inexistant) : 30 secondes.
    ///
    /// Assez pour qu'une rafale d'identifiants au hasard sur un endpoint anonyme ne
    /// traverse pas le cache jusqu'à la base ; assez bref pour qu'un produit tout
    /// juste créé n'attende pas pour exister.
    /// </summary>
    public static readonly TimeSpan MissTtl = TimeSpan.FromSeconds(30);
}
