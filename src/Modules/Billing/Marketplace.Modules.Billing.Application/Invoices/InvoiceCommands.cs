using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Billing.Application.Abstractions;
using Marketplace.Modules.Billing.Domain.Invoices;

namespace Marketplace.Modules.Billing.Application.Invoices;

/// <summary>Crée une facture brouillon pour un vendeur sur une période.</summary>
public sealed record CreateInvoiceCommand(Guid SellerId, DateTime PeriodStartUtc, DateTime PeriodEndUtc, string Currency) : ICommand<Guid>;

/// <summary>Ajoute une ligne à une facture brouillon.</summary>
public sealed record AddInvoiceLineCommand(Guid InvoiceId, string Description, decimal Amount) : ICommand;

/// <summary>Émet une facture (passe de brouillon à émise).</summary>
public sealed record IssueInvoiceCommand(Guid InvoiceId) : ICommand;

/// <summary>Marque une facture émise comme payée.</summary>
public sealed record MarkInvoicePaidCommand(Guid InvoiceId) : ICommand;

internal sealed class CreateInvoiceCommandHandler : ICommandHandler<CreateInvoiceCommand, Guid>
{
    private readonly IInvoiceRepository _repository;
    private readonly IBillingUnitOfWork _unitOfWork;

    public CreateInvoiceCommandHandler(IInvoiceRepository repository, IBillingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateInvoiceCommand command, CancellationToken cancellationToken)
    {
        var result = Invoice.Create(command.SellerId, command.PeriodStartUtc, command.PeriodEndUtc, command.Currency);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _repository.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result.Value.Id.Value;
    }
}

internal abstract class InvoiceMutationHandlerBase
{
    protected readonly IInvoiceRepository Repository;
    protected readonly IBillingUnitOfWork UnitOfWork;

    protected InvoiceMutationHandlerBase(IInvoiceRepository repository, IBillingUnitOfWork unitOfWork)
    {
        Repository = repository;
        UnitOfWork = unitOfWork;
    }

    protected async Task<Result> MutateAsync(Guid invoiceId, Func<Invoice, Result> mutate, CancellationToken ct)
    {
        var invoice = await Repository.GetByIdAsync(new InvoiceId(invoiceId), ct);
        if (invoice is null)
        {
            return Result.Failure(Error.NotFound("billing.invoice.not_found", "Facture introuvable."));
        }

        var result = mutate(invoice);
        if (result.IsFailure)
        {
            return result;
        }

        await UnitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class AddInvoiceLineCommandHandler : InvoiceMutationHandlerBase, ICommandHandler<AddInvoiceLineCommand>
{
    public AddInvoiceLineCommandHandler(IInvoiceRepository repository, IBillingUnitOfWork unitOfWork) : base(repository, unitOfWork) { }

    public Task<Result> Handle(AddInvoiceLineCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.InvoiceId, i => i.AddLine(command.Description, command.Amount), cancellationToken);
}

internal sealed class IssueInvoiceCommandHandler : InvoiceMutationHandlerBase, ICommandHandler<IssueInvoiceCommand>
{
    public IssueInvoiceCommandHandler(IInvoiceRepository repository, IBillingUnitOfWork unitOfWork) : base(repository, unitOfWork) { }

    public Task<Result> Handle(IssueInvoiceCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.InvoiceId, i => i.Issue(), cancellationToken);
}

internal sealed class MarkInvoicePaidCommandHandler : InvoiceMutationHandlerBase, ICommandHandler<MarkInvoicePaidCommand>
{
    public MarkInvoicePaidCommandHandler(IInvoiceRepository repository, IBillingUnitOfWork unitOfWork) : base(repository, unitOfWork) { }

    public Task<Result> Handle(MarkInvoicePaidCommand command, CancellationToken cancellationToken)
        => MutateAsync(command.InvoiceId, i => i.MarkPaid(), cancellationToken);
}
