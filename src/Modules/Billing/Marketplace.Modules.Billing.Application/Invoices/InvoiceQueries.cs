using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Billing.Contracts;
using Marketplace.Modules.Billing.Domain.Invoices;

namespace Marketplace.Modules.Billing.Application.Invoices;

/// <summary>Récupère une facture par son identifiant.</summary>
public sealed record GetInvoiceQuery(Guid InvoiceId) : IQuery<InvoiceSummary>;

/// <summary>Liste les factures d'un vendeur.</summary>
public sealed record ListInvoicesBySellerQuery(Guid SellerId) : IQuery<IReadOnlyList<InvoiceSummary>>;

internal static class InvoiceMapper
{
    public static InvoiceSummary ToSummary(Invoice i) => new(
        i.Id.Value, i.SellerId, i.PeriodStartUtc, i.PeriodEndUtc, i.Currency, i.TotalAmount, i.Status.ToString());
}

internal sealed class GetInvoiceQueryHandler : IQueryHandler<GetInvoiceQuery, InvoiceSummary>
{
    private readonly IInvoiceRepository _repository;

    public GetInvoiceQueryHandler(IInvoiceRepository repository) => _repository = repository;

    public async Task<Result<InvoiceSummary>> Handle(GetInvoiceQuery query, CancellationToken cancellationToken)
    {
        var invoice = await _repository.GetByIdAsync(new InvoiceId(query.InvoiceId), cancellationToken);
        return invoice is null
            ? Error.NotFound("billing.invoice.not_found", "Facture introuvable.")
            : InvoiceMapper.ToSummary(invoice);
    }
}

internal sealed class ListInvoicesBySellerQueryHandler : IQueryHandler<ListInvoicesBySellerQuery, IReadOnlyList<InvoiceSummary>>
{
    private readonly IInvoiceRepository _repository;

    public ListInvoicesBySellerQueryHandler(IInvoiceRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<InvoiceSummary>>> Handle(ListInvoicesBySellerQuery query, CancellationToken cancellationToken)
    {
        var invoices = await _repository.ListBySellerAsync(query.SellerId, cancellationToken);
        IReadOnlyList<InvoiceSummary> summaries = invoices.Select(InvoiceMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}
