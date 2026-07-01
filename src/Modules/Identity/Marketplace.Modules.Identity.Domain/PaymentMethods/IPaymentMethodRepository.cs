namespace Marketplace.Modules.Identity.Domain.PaymentMethods;

/// <summary>Accès au stockage des moyens de paiement enregistrés.</summary>
public interface IPaymentMethodRepository
{
    Task AddAsync(PaymentMethod paymentMethod, CancellationToken cancellationToken = default);

    void Remove(PaymentMethod paymentMethod);

    Task<PaymentMethod?> GetByIdAsync(PaymentMethodId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentMethod>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
