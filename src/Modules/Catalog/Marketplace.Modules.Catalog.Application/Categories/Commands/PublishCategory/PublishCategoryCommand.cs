using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Catalog.Application.Categories.Commands.PublishCategory;

/// <summary>
/// Publie une catégorie (Draft -&gt; Published), la rendant visible dans l'arbre.
///
/// <para>
/// <c>IncludeDescendants</c> étend l'opération à TOUTE la descendance, à n'importe
/// quelle profondeur. Publier « Animaux » seule donne une rubrique vide côté client :
/// ses sous-catégories restent invisibles, et rien ne le signale à l'administrateur —
/// il faut parcourir l'application pour s'en apercevoir. Une taxonomie se publie
/// normalement par branche entière.
/// </para>
///
/// <para>
/// Renvoie le nombre de catégories effectivement publiées, celle-ci comprise.
/// </para>
/// </summary>
public sealed record PublishCategoryCommand(Guid CategoryId, bool IncludeDescendants = false) : ICommand<int>;
