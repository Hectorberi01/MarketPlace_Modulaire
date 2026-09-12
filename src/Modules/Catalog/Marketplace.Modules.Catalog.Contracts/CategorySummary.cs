namespace Marketplace.Modules.Catalog.Contracts;

public sealed record CategorySummary(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Slug,
    string Path,
    string Status,
    string? ImageUrl,
    // ═══════════════════════════════════════════════════════════════════════════════
    // LE SCHÉMA EFFECTIF : CELUI DE LA CATÉGORIE **ET DE TOUS SES ANCÊTRES**.
    //
    // La colonne `attribute_schema` était un jsonb que personne ne lisait. Elle pilote
    // désormais la validation des attributs des produits — et pour que le vendeur
    // puisse s'y conformer, il faut d'abord qu'il sache ce qui est attendu. C'est ce
    // que ce champ apporte à l'application vendeur : de quoi dessiner le formulaire
    // (libellés, types, listes de valeurs, champs obligatoires) au lieu d'un champ
    // « attributs » libre où chacun invente ses clés.
    //
    // Liste VIDE = la branche n'impose rien.
    //
    // IL NE PORTAIT QUE LES ATTRIBUTS DE LA CATÉGORIE ELLE-MÊME. Un produit
    // rangé dans « Informatique › Ordinateurs › Ordinateurs portables » ne voyait donc
    // que ceux de la feuille, alors que les attributs communs — marque, garantie, pays
    // de fabrication — se posent justement sur les étages du haut.
    //
    // L'HÉRITAGE EST RÉSOLU À LA LECTURE, JAMAIS RECOPIÉ. Chaque catégorie ne stocke
    // que ce qu'elle déclare ; la composition se refait à partir du chemin matérialisé.
    // C'est ce qui rend un attribut ajouté sur un parent immédiatement attendu dans
    // toutes les feuilles dessous, sans repasser sur aucune d'elles.
    //
    // POURQUOI CE CHAMP-LÀ ET PAS UN NOUVEAU. Tous les clients existants — console
    // vendeur, app mobile, assistant de création — lisent déjà celui-ci pour dessiner
    // le formulaire produit. Ils reçoivent donc l'héritage sans une ligne de code, ce
    // qui est exactement le but. Les deux champs ci-dessous servent l'ADMINISTRATION,
    // qui doit distinguer ce qu'elle peut modifier de ce qu'elle subit.
    // ═══════════════════════════════════════════════════════════════════════════════
    IReadOnlyList<CategoryAttributeSummary>? AttributeSchema = null,
    // Faux = le produit peut porter des attributs hors schéma. Vrai par défaut : un
    // schéma décrit ce qu'on attend, pas nécessairement tout ce qui est permis.
    //
    // COMPOSÉ, LUI AUSSI, ET LE PLUS STRICT L'EMPORTE : si un seul étage de la branche
    // refuse les clés inconnues, la branche entière les refuse. Un parent qui ferme son
    // jeu d'attributs ne peut pas être rouvert par un enfant — ce serait contourner la
    // règle en créant une sous-catégorie.
    bool AllowUnknownAttributes = true,
    /// <summary>
    /// Les attributs que CETTE catégorie déclare. Les seuls qu'on puisse y modifier.
    /// </summary>
    IReadOnlyList<CategoryAttributeSummary>? OwnAttributes = null,
    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// « ATTRIBUTS HORS SCHÉMA » TEL QUE **CETTE** CATÉGORIE LE DÉCLARE.
    ///
    /// À ne pas confondre avec <c>AllowUnknownAttributes</c>, qui est le COMPOSÉ de la
    /// branche (le plus strict l'emporte). La distinction n'est pas décorative : la
    /// console d'administration précharge ce drapeau dans son formulaire et le
    /// RÉENVOIE à chaque enregistrement, y compris pour un simple renommage.
    ///
    /// Sans ce champ, elle rechargeait la valeur composée : il suffisait qu'un parent
    /// ferme son jeu d'attributs pour que le premier renommage d'un enfant écrive
    /// « allowUnknown: false » dans le schéma PROPRE de cet enfant. Rouvrir le parent
    /// plus tard ne rouvrait alors plus l'enfant — une contrainte héritée devenue
    /// définitive par accident d'interface, exactement le motif que
    /// `PreservationDesMisesAJourTests` surveille.
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// </summary>
    bool OwnAllowUnknownAttributes = true,
    /// <summary>
    /// Ceux qui viennent d'un ancêtre. LECTURE SEULE ici : on les modifie là où ils
    /// sont déclarés, et <c>InheritedFrom</c> dit où aller.
    /// </summary>
    IReadOnlyList<CategoryAttributeSummary>? InheritedAttributes = null,
    /// <summary>
    /// Vrai si la catégorie n'a aucun enfant. SEULES LES FEUILLES ACCUEILLENT DES
    /// PRODUITS : ranger un article dans une catégorie intermédiaire le prive des
    /// attributs de la feuille, donc des filtres par lesquels on le cherche.
    /// </summary>
    bool IsLeaf = true,
    /// <summary>
    /// Clés déclarées à deux étages de la même branche. Vide en fonctionnement normal —
    /// l'écriture refuse désormais une clé déjà portée par un parent — mais les données
    /// antérieures à ce contrôle en portent. La définition de l'ANCÊTRE l'emporte ;
    /// celle citée ici est celle qui est ignorée.
    /// </summary>
    IReadOnlyList<CategoryAttributeConflictSummary>? SchemaConflicts = null);

/// <summary>Une clé déclarée deux fois dans une même branche.</summary>
public sealed record CategoryAttributeConflictSummary(
    string Key,
    Guid IgnoredCategoryId,
    string IgnoredCategoryName,
    string WinningCategoryName);

/// <summary>
/// Les attributs d'UN étage de la branche, tels que le formulaire produit les groupera.
/// </summary>
/// <param name="Inherited">
/// Faux pour la catégorie regardée elle-même, vrai pour ses ancêtres. C'est ce qui
/// permet à l'administration de dessiner « modifiable ici » contre « déclaré plus haut »
/// sans comparer des identifiants.
/// </param>
public sealed record CategoryFormPreviewGroup(
    Guid CategoryId,
    string CategoryName,
    string CategoryPath,
    bool Inherited,
    IReadOnlyList<CategoryAttributeSummary> Attributes);

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════════
/// LE FORMULAIRE PRODUIT QUE CETTE CATÉGORIE ENGENDRE, TEL QUE LE VENDEUR LE VERRA.
///
/// C'est le MODÈLE du formulaire, pas son rendu : la console d'administration le passe
/// au même composant que la console vendeur. Un second rendu, écrit pour l'aperçu,
/// aurait commencé à mentir dès la première modification de l'autre.
/// ═══════════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="IsLeaf">
/// Faux = aucun produit ne peut être créé ici. L'aperçu reste utile dans ce cas : il
/// montre ce que cette catégorie IMPOSE à ses descendants.
/// </param>
/// <param name="RequiredCount">
/// Nombre d'attributs obligatoires, tous étages confondus. C'est le chiffre qui dit, en
/// un coup d'œil, ce qu'on exige d'un vendeur avant qu'il puisse publier.
/// </param>
public sealed record CategoryFormPreview(
    Guid CategoryId,
    string CategoryName,
    string CategoryPath,
    bool IsLeaf,
    bool AllowUnknownAttributes,
    int RequiredCount,
    IReadOnlyList<CategoryFormPreviewGroup> Groups,
    IReadOnlyList<CategoryAttributeConflictSummary> Conflicts);

/// <summary>
/// Un attribut attendu par une catégorie, tel qu'exposé aux applications. Reflet du
/// value object du domaine, sans en être : les Contracts ne dépendent d'aucune entité.
/// </summary>
/// <param name="Key">Clé technique, à utiliser telle quelle dans les attributs du produit.</param>
/// <param name="Label">Libellé à afficher dans le formulaire.</param>
/// <param name="Type">« text », « number », « boolean » ou « enum ».</param>
/// <param name="Required">Le champ doit-il être renseigné ?</param>
/// <param name="Values">Valeurs admises (type « enum » uniquement).</param>
/// <param name="Unit">Unité affichée à côté du champ, si elle a un sens.</param>
/// <param name="Min">Borne inférieure (nombre) ou longueur minimale (texte).</param>
/// <param name="Max">Borne supérieure (nombre) ou longueur maximale (texte).</param>
public sealed record CategoryAttributeSummary(
    string Key,
    string Label,
    string Type,
    bool Required,
    IReadOnlyList<string> Values,
    string? Unit,
    decimal? Min,
    decimal? Max,
    /// <summary>
    /// Nom de la catégorie qui DÉCLARE cet attribut, quand ce n'est pas celle qu'on
    /// regarde. Nul pour un attribut propre.
    ///
    /// Sert deux choses : dire à l'administration où aller pour le modifier, et
    /// permettre au formulaire vendeur de grouper « Informatique » puis « Ordinateurs »
    /// plutôt que d'aligner vingt champs sans hiérarchie visible.
    /// </summary>
    string? InheritedFrom = null,
    /// <summary>Identifiant de cette catégorie — pour un lien direct, sans recherche par nom.</summary>
    Guid? OwnerCategoryId = null);
