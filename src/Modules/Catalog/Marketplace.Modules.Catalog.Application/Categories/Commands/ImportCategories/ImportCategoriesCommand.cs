using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Catalog.Application.Categories.Commands.ImportCategories;

/// <summary>Une ligne du fichier importé : un chemin complet, et l'image du nœud terminal.</summary>
/// <param name="Path">
/// Chemin lisible séparé par « / », ex. « Animaux/Chiens/Alimentation ». Les niveaux
/// intermédiaires absents sont créés au passage : il n'est donc pas nécessaire de les
/// déclarer sur leurs propres lignes, même si le faire ne nuit pas.
/// </param>
/// <param name="ImageUrl">
/// Appliquée au DERNIER segment uniquement. Une même branche apparaissant sur
/// plusieurs lignes, associer l'image à chaque niveau traversé conduirait à ce que la
/// dernière ligne lue écrase silencieusement les précédentes.
/// </param>
public sealed record CategoryImportRow(string Path, string? ImageUrl);

/// <summary>Ce qu'il est advenu d'un nœud de l'arbre pendant l'import.</summary>
/// <param name="Path">Chemin normalisé (slugifié), tel qu'il est stocké.</param>
/// <param name="Label">Chemin lisible, tel qu'il figurait dans le fichier.</param>
/// <param name="Status">« created », « existing » ou « error ».</param>
public sealed record CategoryImportOutcome(string Path, string Label, string Status, string? Message);

/// <summary>Compte rendu global.</summary>
public sealed record CategoryImportReport(
    IReadOnlyList<CategoryImportOutcome> Nodes,
    int Created,
    int Existing,
    int Errors,
    bool DryRun);

/// <summary>
/// Crée une arborescence de catégories à partir de chemins, SANS jamais échouer sur
/// l'existant.
///
/// <para>
/// C'est le point central : importer « Animaux/Chiens/Alimentation » puis
/// « Animaux/Chats/Alimentation » doit créer les deux, en réutilisant « Animaux »
/// et sans se plaindre du doublon apparent. Chaque segment est résolu par son CHEMIN
/// COMPLET, jamais par son seul nom.
/// </para>
///
/// <para>
/// L'opération est donc IDEMPOTENTE : rejouer le même fichier ne crée rien de
/// nouveau et ne renvoie aucune erreur. On peut compléter un catalogue par retouches
/// successives du même tableur, sans tenir de registre de ce qui a déjà été chargé.
/// </para>
///
/// <para>
/// <c>DryRun</c> calcule le compte rendu sans rien écrire : c'est ce qui alimente
/// l'aperçu avant validation.
/// </para>
///
/// <para>
/// Les catégories créées naissent en brouillon. Les rendre visibles reste un geste
/// délibéré — « Publier avec les sous-catégories » sur la racine de la branche.
/// </para>
/// </summary>
public sealed record ImportCategoriesCommand(
    IReadOnlyList<CategoryImportRow> Rows,
    bool DryRun = false) : ICommand<CategoryImportReport>;
