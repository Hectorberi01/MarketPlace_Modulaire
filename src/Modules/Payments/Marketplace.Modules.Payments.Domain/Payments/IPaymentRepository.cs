namespace Marketplace.Modules.Payments.Domain.Payments;

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken cancellationToken = default);

    Task<Payment?> GetByIdAsync(PaymentId id, CancellationToken cancellationToken = default);

    Task<Payment?> GetByOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Retrouve un paiement par sa référence PSP (corrélation webhook / retour).</summary>
    Task<Payment?> GetByProviderReferenceAsync(string providerReference, CancellationToken cancellationToken = default);
}
