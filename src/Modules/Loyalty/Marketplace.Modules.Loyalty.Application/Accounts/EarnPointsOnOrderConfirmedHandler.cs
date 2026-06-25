using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Loyalty.Application.Abstractions;
using Marketplace.Modules.Loyalty.Domain.Accounts;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Loyalty.Application.Accounts;

/// <summary>
/// Crédite des points de fidélité à la confirmation d'une commande : 1 point par
/// tranche de 100 XOF du total. Le compte est créé à la volée et l'opération est
/// idempotente par commande (pas de double crédit si l'event est rejoué).
/// </summary>
public sealed class EarnPointsOnOrderConfirmedHandler : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    private const decimal PointsPerCurrencyUnit = 100m;

    private readonly ILoyaltyAccountRepository _repository;
    private readonly IOrderingModuleApi _orderingModuleApi;
    private readonly ILoyaltyUnitOfWork _unitOfWork;

    public EarnPointsOnOrderConfirmedHandler(
        ILoyaltyAccountRepository repository, IOrderingModuleApi orderingModuleApi, ILoyaltyUnitOfWork unitOfWork)
    {
        _repository = repository;
        _orderingModuleApi = orderingModuleApi;
        _unitOfWork = unitOfWork;
    }

    public async Task HandleAsync(OrderConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var order = await _orderingModuleApi.GetOrderAsync(integrationEvent.OrderId, cancellationToken);
        if (order is null)
        {
            return;
        }

        var points = (int)Math.Floor(order.GrandTotal / PointsPerCurrencyUnit);
        if (points <= 0)
        {
            return;
        }

        var account = await _repository.GetByUserAsync(order.BuyerId, cancellationToken);
        if (account is null)
        {
            var created = LoyaltyAccount.Create(order.BuyerId);
            if (created.IsFailure)
            {
                return;
            }

            account = created.Value;
            await _repository.AddAsync(account, cancellationToken);
        }

        if (account.HasEarnedForOrder(order.Id))
        {
            return;
        }

        account.EarnPoints(points, PointReason.Purchase, order.Id);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
