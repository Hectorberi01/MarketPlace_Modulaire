using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Settlement.Domain.Batches.Events;

namespace Marketplace.Modules.Settlement.Domain.Batches;

/// <summary>Identité forte d'un lot de reversement.</summary>
public readonly record struct SettlementBatchId(Guid Value)
{
    public static SettlementBatchId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Statut d'un lot de reversement.</summary>
public enum SettlementStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    PartiallyFailed = 3
}

/// <summary>Statut d'un reversement individuel.</summary>
public enum PayoutStatus
{
    Scheduled = 0,
    Paid = 1,
    Failed = 2
}

/// <summary>Reversement à un vendeur : net après commission. Entité enfant du lot.</summary>
public sealed class Payout : Entity<Guid>
{
    private Payout()
    {
    }

    internal Payout(Guid id, Guid sellerId, decimal grossAmount, decimal commissionAmount, decimal netAmount, string currency)
        : base(id)
    {
        SellerId = sellerId;
        GrossAmount = grossAmount;
        CommissionAmount = commissionAmount;
        NetAmount = netAmount;
        Currency = currency;
        Status = PayoutStatus.Scheduled;
    }

    public Guid SellerId { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal CommissionAmount { get; private set; }
    public decimal NetAmount { get; private set; }
    public string Currency { get; private set; } = default!;
    public PayoutStatus Status { get; private set; }
    public string? ProviderRef { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }

    internal void MarkPaid(string providerRef)
    {
        Status = PayoutStatus.Paid;
        ProviderRef = providerRef;
        PaidAtUtc = DateTime.UtcNow;
    }

    internal void MarkFailed() => Status = PayoutStatus.Failed;
}

/// <summary>
/// Lot de reversements agrégés par période. Boucle le cycle financier vendeur :
/// regroupe les gains nets en un reversement par vendeur, suivis jusqu'au
/// versement effectif (MoMo/Wave/banque). Agrégat racine : possède ses payouts.
/// </summary>
public sealed class SettlementBatch : AggregateRoot<SettlementBatchId>
{
    private readonly List<Payout> _payouts = new();

    private SettlementBatch()
    {
    }

    private SettlementBatch(SettlementBatchId id, DateTime periodStartUtc, DateTime periodEndUtc, string currency)
        : base(id)
    {
        PeriodStartUtc = periodStartUtc;
        PeriodEndUtc = periodEndUtc;
        Currency = currency;
        Status = SettlementStatus.Pending;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public DateTime PeriodStartUtc { get; private set; }
    public DateTime PeriodEndUtc { get; private set; }
    public string Currency { get; private set; } = default!;
    public decimal TotalNet { get; private set; }
    public SettlementStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public IReadOnlyCollection<Payout> Payouts => _payouts.AsReadOnly();

    public static Result<SettlementBatch> Create(DateTime periodStartUtc, DateTime periodEndUtc, string currency)
    {
        if (periodEndUtc <= periodStartUtc)
        {
            return Error.Validation("settlement.period_invalid", "La période de reversement est invalide.");
        }

        return new SettlementBatch(SettlementBatchId.New(), periodStartUtc, periodEndUtc, currency.Trim().ToUpperInvariant());
    }

    public Guid AddPayout(Guid sellerId, decimal grossAmount, decimal commissionAmount, decimal netAmount)
    {
        var payout = new Payout(Guid.NewGuid(), sellerId, grossAmount, commissionAmount, netAmount, Currency);
        _payouts.Add(payout);
        TotalNet = _payouts.Sum(p => p.NetAmount);
        return payout.Id;
    }

    public Result MarkPayoutPaid(Guid payoutId, string providerRef)
    {
        var payout = _payouts.FirstOrDefault(p => p.Id == payoutId);
        if (payout is null)
        {
            return Result.Failure(Error.NotFound("settlement.payout.not_found", "Reversement introuvable dans ce lot."));
        }

        if (payout.Status == PayoutStatus.Paid)
        {
            return Result.Success();
        }

        payout.MarkPaid(providerRef);
        Raise(new PayoutPaidDomainEvent(Id.Value, payout.Id, payout.SellerId, payout.NetAmount, payout.Currency));
        RefreshStatus();
        return Result.Success();
    }

    public Result MarkPayoutFailed(Guid payoutId)
    {
        var payout = _payouts.FirstOrDefault(p => p.Id == payoutId);
        if (payout is null)
        {
            return Result.Failure(Error.NotFound("settlement.payout.not_found", "Reversement introuvable dans ce lot."));
        }

        payout.MarkFailed();
        RefreshStatus();
        return Result.Success();
    }

    private void RefreshStatus()
    {
        if (_payouts.All(p => p.Status == PayoutStatus.Paid))
        {
            Status = SettlementStatus.Completed;
        }
        else if (_payouts.Any(p => p.Status == PayoutStatus.Failed))
        {
            Status = SettlementStatus.PartiallyFailed;
        }
        else
        {
            Status = SettlementStatus.Processing;
        }
    }
}
