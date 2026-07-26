using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.SetProductTags;

/// <summary>
/// Remplace la liste des tags d'un produit. Utilisé par la curation éditoriale
/// (tableau de bord admin) — notamment pour marquer/démarquer « featured ».
/// </summary>
public sealed record SetProductTagsCommand(Guid ProductId, IReadOnlyList<string> Tags) : ICommand;
