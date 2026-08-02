using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Catalog.Application.Categories.Commands.UnpublishCategory;

/// <summary>
/// Dépublie une catégorie (Published -&gt; Draft), la retirant de l'arbre visible.
///
/// <para>
/// <c>IncludeDescendants</c> étend l'opération à toute la descendance. C'est le
/// pendant nécessaire de la publication en cascade : dépublier « Animaux » seule
/// laisserait ses sous-catégories marquées « publiées » alors que plus rien n'y
/// mène — un état que l'administrateur croit propre et qui ne l'est pas.
/// </para>
///
/// <para>Renvoie le nombre de catégories effectivement dépubliées, celle-ci comprise.</para>
/// </summary>
public sealed record UnpublishCategoryCommand(Guid CategoryId, bool IncludeDescendants = false) : ICommand<int>;
