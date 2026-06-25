using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.AddKybDocument;

/// <summary>Ajoute une pièce justificative KYB (passe la vérification en revue).</summary>
public sealed record AddKybDocumentCommand(Guid SellerId, string Type, string FileUrl) : ICommand<Guid>;
