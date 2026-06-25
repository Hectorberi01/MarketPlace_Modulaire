namespace Marketplace.Modules.Billing.Domain.Invoices;

public interface IInvoiceRepository
{
    Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default);

    Task<Invoice?> GetByIdAsync(InvoiceId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Invoice>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default);
}
