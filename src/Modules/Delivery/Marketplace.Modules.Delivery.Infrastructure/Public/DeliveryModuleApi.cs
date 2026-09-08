using MediatR;
using Marketplace.Modules.Delivery.Application.Couriers;
using Marketplace.Modules.Delivery.Contracts;

namespace Marketplace.Modules.Delivery.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Delivery. Lecture seule :
/// Settlement s'en sert pour savoir où verser l'argent d'un coursier, et rien d'autre
/// n'a besoin d'écrire ici depuis l'extérieur.
/// </summary>
internal sealed class DeliveryModuleApi : IDeliveryModuleApi
{
    private readonly ISender _sender;

    public DeliveryModuleApi(ISender sender) => _sender = sender;

    public async Task<CourierSummary?> GetCourierAsync(Guid courierId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetCourierQuery(courierId), cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<CourierSummary?> GetCourierByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetCourierByUserQuery(userId), cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }
}
