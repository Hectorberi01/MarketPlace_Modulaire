using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.UpdateSellerMetadata;

/// <summary>
/// Met à jour les informations société (metadata) déclarées par le vendeur.
/// <c>null</c> efface la metadata. N'affecte ni le statut ni le KYB.
/// </summary>
public sealed record UpdateSellerMetadataCommand(Guid SellerId, SellerCompanyInfo? Metadata) : ICommand;
