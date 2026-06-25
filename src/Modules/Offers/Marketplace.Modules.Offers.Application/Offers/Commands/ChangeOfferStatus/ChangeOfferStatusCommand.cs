using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.ChangeOfferStatus;

/// <summary>Change le statut d'une offre : Active, Paused ou OutOfStock.</summary>
public sealed record ChangeOfferStatusCommand(Guid OfferId, string Status) : ICommand;
