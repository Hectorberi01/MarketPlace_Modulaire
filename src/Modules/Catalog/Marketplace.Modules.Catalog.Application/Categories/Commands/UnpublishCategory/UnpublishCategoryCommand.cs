using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Catalog.Application.Categories.Commands.UnpublishCategory;

/// <summary>Dépublie une catégorie (Published -> Draft), la retirant de l'arbre visible.</summary>
public sealed record UnpublishCategoryCommand(Guid CategoryId) : ICommand;
