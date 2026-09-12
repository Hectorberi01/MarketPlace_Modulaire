namespace Marketplace.Modules.Catalog.Domain.Products;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// LES TAGS QUI NE SONT PAS À LA MAIN DU VENDEUR.
///
/// Un tag de produit est du texte libre, saisi par le vendeur. AUCUNE FONCTION NE S'EN
/// SERT : la recherche ne les indexe pas — `ProductIndexEntry` n'en porte aucun — et
/// aucun filtre, aucun tri ne les regarde. Ils sont en revanche PROJETÉS :
/// <c>ProductSummary.Tags</c> les rend à tous les BFF, donc ils sortent sur le réseau à
/// chaque fiche produit. « Ils ne servent à rien » et « personne ne les voit » sont deux
/// choses différentes, et seule la première est vraie.
///
/// UN seul fait exception à l'inutilité, et il pilote une surface éditoriale :
/// « featured » place le produit dans la section « Mis en avant » de l'accueil acheteur
/// (<c>CatalogModuleApi.ListFeaturedAsync</c>).
///
/// CE TAG ÉTAIT ÉCRIVABLE PAR LE VENDEUR LUI-MÊME. Le chemin était complet et sans
/// garde. `PUT /seller/products/{id}` transmet le tableau `tags` tel quel ;
/// `POST /seller/products` est multipart et reçoit une CHAÎNE « a,b,c » qu'il découpe
/// et débarrasse des vides — mais ni l'un ni l'autre ne regardait la VALEUR des tags.
/// `Product.Create` et `Product.Update` ne filtraient rien non plus, et l'accueil ne
/// demande qu'un produit actif portant le tag. Un vendeur dont
/// un produit était publié pouvait donc se placer en page d'accueil en tapant
/// « featured » dans un champ de texte libre — celui-là même dont l'écran lui disait
/// qu'il « aide la recherche ».
///
/// CE QUI EST FERMÉ, ET CE QUI NE L'EST PAS. Le tag réservé ne s'attrape plus ni à la
/// création ni à la modification ordinaire : il reste la propriété de la route
/// d'administration dédiée (<c>SetProductTagsCommand</c>), qui garde le contrôle
/// entier. C'est la seule porte qu'emprunte le code d'aujourd'hui, et elle est du bon
/// côté.
///
/// CE QUI RESTE OUVERT, ET IL FAUT LE SAVOIR : <c>Product.Tags</c> est un
/// <c>List&lt;string&gt;</c> au setter privé mais à la LISTE publique. `Tags.Add(...)`
/// contournerait tout ceci. Aucun appelant ne le fait — vérifié sur `src` et `tests` —
/// mais la garantie tient par convention, pas par le système de types. La fermer
/// vraiment demande d'exposer un <c>IReadOnlyList</c> sur champ privé, ce qui touche la
/// configuration EF : à faire avec un compilateur sous la main.
///
/// LE PIÈGE ÉVITÉ, ET IL COMPTE AUTANT QUE LA FAILLE. `Update` REMPLACE la liste de
/// tags. Se contenter de retirer les tags réservés de l'entrée aurait fait DISPARAÎTRE
/// la mise en avant dès que le vendeur corrige le nom de son produit — une sanction
/// silencieuse infligée à l'administration par le geste le plus banal du vendeur. Les
/// tags réservés sont donc RECONDUITS depuis l'état courant, jamais lus depuis
/// l'entrée : le vendeur décide de ses tags à lui, l'administration des siens.
///
/// La comparaison est INSENSIBLE À LA CASSE alors que la lecture ne l'est pas
/// (`'featured' = ANY(tags)` en PostgreSQL). C'est volontaire et dissymétrique :
/// « Featured » n'aurait rien déclenché, mais le laisser passer offrirait au vendeur
/// un tag qui ressemble au tag réservé — et la première correction de la requête de
/// lecture le rendrait effectif.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class ProductTags
{
    /// <summary>
    /// Mise en avant éditoriale, posée par l'administration. Cette constante est la
    /// SEULE définition du tag : la requête de l'accueil s'en sert aussi, pour que le
    /// filtre et la garde ne puissent pas diverger.
    /// </summary>
    public const string Featured = "featured";

    private static readonly string[] Reserves = [Featured];

    /// <summary>Ce tag est-il réservé à l'administration ?</summary>
    public static bool IsReserved(string? tag)
        => tag is not null
           && Reserves.Any(r => string.Equals(r, tag.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Nettoie une liste de tags fournie par un appelant : sans vides, sans espaces
    /// superflus, sans doublons. Ne décide RIEN sur les tags réservés — c'est à
    /// l'appelant de choisir s'il les retire ou les reconduit.
    ///
    /// <para>
    /// `IEnumerable` ET NON `IReadOnlyList` : c'est ce que déclarent `Product.Create` et
    /// `Product.Update`, et il n'existe aucune conversion implicite de l'un vers l'autre.
    /// La première version de ce fichier exigeait `IReadOnlyList` et ne compilait pas.
    /// </para>
    ///
    /// <para>
    /// DÉDUPLICATION ORDINALE, comme avant ce correctif. La tentation était de passer
    /// en insensible à la casse « puisqu'on y est » : cela aurait fait disparaître
    /// « cuir » d'un vendeur qui avait aussi « Cuir », et « promo » d'un administrateur
    /// qui avait aussi « Promo » — une perte de données silencieuse sur un chemin que ce
    /// correctif n'avait aucune raison de toucher. Seule la reconnaissance du tag
    /// RÉSERVÉ est insensible à la casse, et c'est délibéré (voir `IsReserved`).
    /// </para>
    /// </summary>
    public static List<string> Clean(IEnumerable<string>? tags)
        => tags?
               .Where(t => !string.IsNullOrWhiteSpace(t))
               .Select(t => t.Trim())
               .Distinct()
               .ToList()
           ?? [];

    /// <summary>
    /// Tags admissibles depuis une saisie VENDEUR : ceux qu'il fournit, moins les
    /// réservés. Utilisé à la création, où il n'y a aucun tag antérieur à préserver.
    /// </summary>
    public static List<string> FromSeller(IEnumerable<string>? tags)
        => Clean(tags).Where(t => !IsReserved(t)).ToList();

    /// <summary>
    /// Tags après une modification VENDEUR : les siens remplacent les anciens, mais
    /// les tags réservés déjà posés par l'administration sont reconduits.
    /// </summary>
    public static List<string> MergeFromSeller(
        IEnumerable<string>? fournis, IEnumerable<string> existants)
    {
        // `FromSeller` a déjà retiré tout ce qui ressemble à un tag réservé, quelle que
        // soit la casse : la concaténation ne peut donc pas produire de doublon sur ces
        // valeurs-là, et le `Distinct` final reste ordinal comme partout ailleurs.
        var reserves = existants.Where(IsReserved);
        return FromSeller(fournis)
            .Concat(reserves)
            .Distinct()
            .ToList();
    }
}
