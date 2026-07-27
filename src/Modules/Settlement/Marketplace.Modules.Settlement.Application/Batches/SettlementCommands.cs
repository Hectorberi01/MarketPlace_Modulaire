using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Domain.Batches;
using Marketplace.Modules.Settlement.Domain.Earnings;

namespace Marketplace.Modules.Settlement.Application.Batches;

/// <summary>Génère un lot de reversements pour la période : un payout net par vendeur.</summary>
public sealed record RunSettlementCommand(DateTime PeriodStartUtc, DateTime PeriodEndUtc) : ICommand<Guid>;

/// <summary>Marque un reversement comme versé (simule le transfert MoMo/banque).</summary>
public sealed record MarkPayoutPaidCommand(Guid BatchId, Guid PayoutId, string ProviderRef) : ICommand;

public sealed class RunSettlementCommandValidator : AbstractValidator<RunSettlementCommand>
{
    public RunSettlementCommandValidator()
        => RuleFor(c => c.PeriodEndUtc).GreaterThan(c => c.PeriodStartUtc).WithMessage("Période invalide.");
}

internal sealed class RunSettlementCommandHandler : ICommandHandler<RunSettlementCommand, Guid>
{
    private readonly ISellerEarningRepository _earningRepository;
    private readonly ISettlementBatchRepository _batchRepository;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public RunSettlementCommandHandler(
        ISellerEarningRepository earningRepository,
        ISettlementBatchRepository batchRepository,
        ISettlementUnitOfWork unitOfWork)
    {
        _earningRepository = earningRepository;
        _batchRepository = batchRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(RunSettlementCommand command, CancellationToken cancellationToken)
    {
        // On ne reverse que les gains LIBÉRÉS (escrow levé à la livraison), pas les
        // gains encore en escrow (simplement confirmés).
        var earnings = await _earningRepository.ListReleasedInPeriodAsync(command.PeriodStartUtc, command.PeriodEndUtc, cancellationToken);
        if (earnings.Count == 0)
        {
            return Result.Failure<Guid>(Error.Conflict("settlement.nothing_to_settle", "Aucun gain payable (livré) à reverser sur cette période."));
        }

        var currency = earnings[0].Currency;

        // Un lot est STRICTEMENT mono-devise : le batch porte une seule devise et
        // additionne des montants — mélanger XOF et une autre devise dans une même
        // somme donnerait un total dénué de sens. On ne règle donc que les gains de
        // CETTE devise ; ceux d'une autre devise restent « Released » pour un prochain
        // lot (aucun effet en XOF pur aujourd'hui, mais le bug latent est fermé).
        var settleable = earnings.Where(e => e.Currency == currency).ToList();

        var batchResult = SettlementBatch.Create(command.PeriodStartUtc, command.PeriodEndUtc, currency);
        if (batchResult.IsFailure)
        {
            return Result.Failure<Guid>(batchResult.Error);
        }

        var batch = batchResult.Value;

        foreach (var group in settleable.GroupBy(e => e.SellerId))
        {
            batch.AddPayout(
                group.Key,
                group.Sum(e => e.GrossAmount),
                group.Sum(e => e.CommissionAmount),
                group.Sum(e => e.NetAmount));
        }

        foreach (var earning in settleable)
        {
            earning.MarkSettled(batch.Id.Value);
        }

        await _batchRepository.AddAsync(batch, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return batch.Id.Value;
    }
}

internal sealed class MarkPayoutPaidCommandHandler : ICommandHandler<MarkPayoutPaidCommand>
{
    private readonly ISettlementBatchRepository _batchRepository;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public MarkPayoutPaidCommandHandler(ISettlementBatchRepository batchRepository, ISettlementUnitOfWork unitOfWork)
    {
        _batchRepository = batchRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(MarkPayoutPaidCommand command, CancellationToken cancellationToken)
    {
        var batch = await _batchRepository.GetByIdAsync(new SettlementBatchId(command.BatchId), cancellationToken);
        if (batch is null)
        {
            return Result.Failure(Error.NotFound("settlement.batch.not_found", "Lot de reversement introuvable."));
        }

        var result = batch.MarkPayoutPaid(command.PayoutId, command.ProviderRef);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
