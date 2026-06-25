using MediatR;
using Marketplace.Modules.Payments.Application.Payments.Queries;
using Marketplace.Modules.Payments.Contracts;

namespace Marketplace.Modules.Payments.Infrastructure.Public;

/// <summary>Implémentation in-process de l'API publique du module Payments.</summary>
internal sealed class PaymentsModuleApi : IPaymentsModuleApi
{
    private readonly ISender _sender;

    public PaymentsModuleApi(ISender sender) => _sender = sender;

    public async Task<PaymentSummary?> GetPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetPaymentQuery(paymentId), cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<PaymentSummary?> GetPaymentByOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var result = await _sender.Send(new GetPaymentByOrderQuery(orderId), cancellationToken);
        return result.IsSuccess ? result.Value : null;
    }
}
