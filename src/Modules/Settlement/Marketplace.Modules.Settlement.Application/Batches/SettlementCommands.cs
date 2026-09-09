using FluentValidation;
using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Application.Wallets;
using Marketplace.Modules.Settlement.Domain.Batches;
using Marketplace.Modules.Settlement.Domain.Earnings;

namespace Marketplace.Modules.Settlement.Application.Batches;

/// <summary>
/// Génère un lot de reversements pour la période : un payout net par vendeur.
/// <paramref name="SellerId"/> restreint le lot à UN vendeur ; null = tous.
/// </summary>
public sealed record RunSettlementCommand(DateTime PeriodStartUtc, DateTime PeriodEndUtc, Guid? SellerId = null) : ICommand<Guid>;

/// <summary>Marque un reversement comme versé (simule le transfert MoMo/banque).</summary>
public sealed record MarkPayoutPaidCommand(Guid BatchId, Guid PayoutId, string ProviderRef) : ICommand;

/// <summary>
/// Annule un lot encore en attente : le lot passe « Cancelled » et ses gains
/// redeviennent payables (ils pourront entrer dans un prochain lot).
/// </summary>
public sealed record CancelSettlementBatchCommand(Guid BatchId) : ICommand;

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
        var released = await _earningRepository.ListReleasedInPeriodAsync(command.PeriodStartUtc, command.PeriodEndUtc, cancellationToken);

        // Ciblage d'UN vendeur : on filtre après lecture plutôt que d'ajouter une
        // surcharge au repository — la période borne déjà fortement le volume.
        var earnings = command.SellerId is { } sellerId
            ? released.Where(e => e.SellerId == sellerId).ToList()
            : released;

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

internal sealed class CancelSettlementBatchCommandHandler : ICommandHandler<CancelSettlementBatchCommand>
{
    private readonly ISettlementBatchRepository _batchRepository;
    private readonly ISellerEarningRepository _earningRepository;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public CancelSettlementBatchCommandHandler(
        ISettlementBatchRepository batchRepository,
        ISellerEarningRepository earningRepository,
        ISettlementUnitOfWork unitOfWork)
    {
        _batchRepository = batchRepository;
        _earningRepository = earningRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(CancelSettlementBatchCommand command, CancellationToken cancellationToken)
    {
        var batch = await _batchRepository.GetByIdAsync(new SettlementBatchId(command.BatchId), cancellationToken);
        if (batch is null)
        {
            return Result.Failure(Error.NotFound("settlement.batch.not_found", "Lot de reversement introuvable."));
        }

        // Le domaine refuse l'annulation si un versement est déjà parti.
        var result = batch.Cancel();
        if (result.IsFailure)
        {
            return result;
        }

        // Les gains du lot redeviennent payables : sans ça, ils resteraient « soldés »
        // pour un lot annulé — donc jamais reversés, et invisibles des lots suivants.
        var earnings = await _earningRepository.ListByBatchAsync(command.BatchId, cancellationToken);
        foreach (var earning in earnings)
        {
            earning.Unsettle();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// CLÔTURE D'UN REVERSEMENT DE LOT — ET DÉBIT DU VENDEUR.
///
/// Ce handler ne faisait que changer un statut. Le portefeuille du vendeur n'était
/// jamais touché : après un lot payé, son solde retirable restait intact. Il
/// demandait alors un retrait, l'admin l'approuvait, et le même gain sortait une
/// seconde fois. Deux circuits de paiement pour un seul gain, exposés sur deux
/// hôtes, sans le moindre contrôle entre eux.
///
/// Le débit les réunit. Les deux rails puisent désormais dans le même solde, et le
/// second passage est fermé par `Withdrawal.Create`, qui refuse déjà un retrait
/// supérieur au disponible — sans qu'aucune règle nouvelle n'ait à l'énoncer.
///
/// L'ORDRE COMPTE. La transition d'agrégat vient d'abord : elle est idempotente
/// (un reversement déjà payé renvoie succès sans rien faire), et c'est elle qui
/// décide si un débit doit avoir lieu. Le rejeu ne débite donc jamais deux fois —
/// et le verrou de grand livre en second rideau le garantit même si quelqu'un
/// assouplit un jour l'agrégat.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class MarkPayoutPaidCommandHandler : ICommandHandler<MarkPayoutPaidCommand>
{
    private readonly ISettlementBatchRepository _batchRepository;
    private readonly WalletMutations _wallets;
    private readonly ISettlementUnitOfWork _unitOfWork;
    private readonly ILogger<MarkPayoutPaidCommandHandler> _logger;

    public MarkPayoutPaidCommandHandler(
        ISettlementBatchRepository batchRepository,
        WalletMutations wallets,
        ISettlementUnitOfWork unitOfWork,
        ILogger<MarkPayoutPaidCommandHandler> logger)
    {
        _batchRepository = batchRepository;
        _wallets = wallets;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(MarkPayoutPaidCommand command, CancellationToken cancellationToken)
    {
        var batch = await _batchRepository.GetByIdAsync(new SettlementBatchId(command.BatchId), cancellationToken);
        if (batch is null)
        {
            return Result.Failure(Error.NotFound("settlement.batch.not_found", "Lot de reversement introuvable."));
        }

        var payout = batch.Payouts.FirstOrDefault(p => p.Id == command.PayoutId);
        if (payout is null)
        {
            return Result.Failure(Error.NotFound("settlement.payout.not_found", "Reversement introuvable dans ce lot."));
        }

        // Lu AVANT la transition : une fois `MarkPayoutPaid` passé, le statut ne dit
        // plus si ce rejeu est le premier.
        var alreadyPaid = payout.Status == PayoutStatus.Paid;

        var result = batch.MarkPayoutPaid(command.PayoutId, command.ProviderRef);
        if (result.IsFailure)
        {
            return result;
        }

        if (!alreadyPaid && !await _wallets.BatchPayoutAlreadyDebitedAsync(command.PayoutId, cancellationToken))
        {
            var remaining = await _wallets.DebitSellerBatchPayoutAsync(
                payout.SellerId, payout.NetAmount, payout.Currency, payout.Id, cancellationToken);

            // ─────────────────────────────────────────────────────────────────────
            // UN SOLDE NÉGATIF ICI SIGNIFIE QUE LE VENDEUR A DÉJÀ ÉTÉ PAYÉ.
            //
            // Le virement de ce lot est un fait accompli — l'administrateur l'a
            // exécuté à la main avant d'appeler cette route. Le refuser n'aurait
            // rappelé aucun argent, seulement effacé la trace de sa sortie. On
            // enregistre donc, et on crie : c'est la seule combinaison qui laisse
            // des livres justes ET un humain averti.
            // ─────────────────────────────────────────────────────────────────────
            if (remaining < 0m)
            {
                _logger.LogCritical(
                    "DOUBLE PAIEMENT VENDEUR : le reversement {PayoutId} du lot {BatchId} a porté le solde "
                    + "disponible du vendeur {SellerId} à {Balance} {Currency}. Ce gain avait déjà été retiré. "
                    + "Aucune correction automatique n'est appliquée.",
                    payout.Id, command.BatchId, payout.SellerId, remaining, payout.Currency);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
