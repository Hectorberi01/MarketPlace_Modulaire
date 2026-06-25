namespace Marketplace.Modules.Payments.Contracts;

/// <summary>
/// API in-process publique du module Payments. Permet aux autres modules de lire
/// l'état d'un paiement sans accéder à sa base.
/// </summary>
public interface IPaymentsModuleApi
{
    Task<PaymentSummary?> GetPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);

    Task<PaymentSummary?> GetPaymentByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
}
