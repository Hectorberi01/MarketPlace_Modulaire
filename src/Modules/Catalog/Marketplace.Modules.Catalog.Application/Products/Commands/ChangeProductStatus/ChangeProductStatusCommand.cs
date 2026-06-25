using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.ChangeProductStatus;

/// <summary>
/// Change le statut d'un produit. Cible attendue : « Active » (publier),
/// « Archived » (retirer) ou « Draft » (repasser en brouillon).
/// </summary>
public sealed record ChangeProductStatusCommand(Guid ProductId, string Status) : ICommand;
