using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.SetHandlingTime;

/// <summary>Modifie le délai de préparation (jours) d'une offre.</summary>
public sealed record SetHandlingTimeCommand(Guid OfferId, int HandlingTime) : ICommand;
