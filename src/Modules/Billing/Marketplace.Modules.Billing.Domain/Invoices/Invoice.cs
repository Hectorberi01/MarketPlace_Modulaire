using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Billing.Domain.Invoices;

/// <summary>Identité forte d'une facture.</summary>
public readonly record struct InvoiceId(Guid Value)
{
    public static InvoiceId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Statut d'une facture de frais émise au vendeur.</summary>
public enum InvoiceStatus
{
    Draft = 0,
    Issued = 1,
    Paid = 2
}

/// <summary>Ligne de facture (commission, service…). Entité enfant.</summary>
public sealed class InvoiceLine : Entity<Guid>
{
    private InvoiceLine()
    {
    }

    internal InvoiceLine(Guid id, string description, decimal amount)
        : base(id)
    {
        Description = description;
        Amount = amount;
    }

    public string Description { get; private set; } = default!;
    public decimal Amount { get; private set; }
}

/// <summary>
/// Facture de frais émise à un vendeur pour une période (commissions, services).
/// Agrégat racine : possède ses lignes.
/// </summary>
public sealed class Invoice : AggregateRoot<InvoiceId>
{
    private readonly List<InvoiceLine> _lines = new();

    private Invoice()
    {
    }

    private Invoice(InvoiceId id, Guid sellerId, DateTime periodStartUtc, DateTime periodEndUtc, string currency)
        : base(id)
    {
        SellerId = sellerId;
        PeriodStartUtc = periodStartUtc;
        PeriodEndUtc = periodEndUtc;
        Currency = currency;
        Status = InvoiceStatus.Draft;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid SellerId { get; private set; }
    public DateTime PeriodStartUtc { get; private set; }
    public DateTime PeriodEndUtc { get; private set; }
    public string Currency { get; private set; } = default!;
    public decimal TotalAmount { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? IssuedAtUtc { get; private set; }

    public IReadOnlyCollection<InvoiceLine> Lines => _lines.AsReadOnly();

    public static Result<Invoice> Create(Guid sellerId, DateTime periodStartUtc, DateTime periodEndUtc, string currency)
    {
        if (sellerId == Guid.Empty)
        {
            return Error.Validation("billing.invoice.seller_required", "Le vendeur est obligatoire.");
        }

        if (periodEndUtc <= periodStartUtc)
        {
            return Error.Validation("billing.invoice.period_invalid", "La période de facturation est invalide.");
        }

        return new Invoice(InvoiceId.New(), sellerId, periodStartUtc, periodEndUtc, currency.Trim().ToUpperInvariant());
    }

    public Result AddLine(string description, decimal amount)
    {
        if (Status != InvoiceStatus.Draft)
        {
            return Result.Failure(Error.Conflict("billing.invoice.not_draft", "Seule une facture brouillon est modifiable."));
        }

        _lines.Add(new InvoiceLine(Guid.NewGuid(), description.Trim(), amount));
        TotalAmount = _lines.Sum(l => l.Amount);
        return Result.Success();
    }

    public Result Issue()
    {
        if (Status != InvoiceStatus.Draft)
        {
            return Result.Failure(Error.Conflict("billing.invoice.not_draft", "La facture n'est pas un brouillon."));
        }

        if (_lines.Count == 0)
        {
            return Result.Failure(Error.Conflict("billing.invoice.empty", "Impossible d'émettre une facture vide."));
        }

        Status = InvoiceStatus.Issued;
        IssuedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result MarkPaid()
    {
        if (Status != InvoiceStatus.Issued)
        {
            return Result.Failure(Error.Conflict("billing.invoice.not_issued", "Seule une facture émise peut être payée."));
        }

        Status = InvoiceStatus.Paid;
        return Result.Success();
    }
}
