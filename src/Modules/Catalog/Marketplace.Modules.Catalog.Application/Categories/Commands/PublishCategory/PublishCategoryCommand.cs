using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Catalog.Application.Categories.Commands.PublishCategory;

/// <summary>Publie une catégorie (Draft -> Published), la rendant visible dans l'arbre.</summary>
public sealed record PublishCategoryCommand(Guid CategoryId) : ICommand;
