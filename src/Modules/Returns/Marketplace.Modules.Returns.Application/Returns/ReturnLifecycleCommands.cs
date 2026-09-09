using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Returns.Application.Abstractions;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Settlement.Contracts;
using Marketplace.Modules.Returns.Domain.Returns;

namespace Marketplace.Modules.Returns.Application.Returns;

/// <summary>Approuve une demande de retour (vendeur / admin).</summary>
public sealed record ApproveReturnCommand(Guid ReturnRequestId) : ICommand;

/// <summary>Rejette une demande de retour.</summary>
public sealed record RejectReturnCommand(Guid ReturnRequestId, string Reason) : ICommand;

/// <summary>Renseigne le colis retour (transporteur + suivi).</summary>
public sealed record AddReturnTrackingCommand(Guid ReturnRequestId, string Carrier, string TrackingNumber) : ICommand;

/// <summary>Marque le colis retour comme réceptionné.</summary>
public sealed record MarkReturnReceivedCommand(Guid ReturnRequestId) : ICommand;

/// <summary>
/// VALIDE le remboursement et son montant (vendeur ou admin). L'argent ne bouge pas.
///
/// Renommée depuis « RefundReturnCommand » — et ce n'est pas cosmétique. L'ancien nom
/// promettait un remboursement ; le code ne faisait que changer un statut. Le nom
/// mentait autant que le code.
/// </summary>
public sealed record ApproveReturnRefundCommand(Guid ReturnRequestId, decimal Amount) : ICommand;

/// <summary>
/// CONFIRME que l'argent est parti (admin uniquement), référence FedaPay à l'appui.
///
/// Réservé à l'admin parce que lui seul a accès au tableau de bord FedaPay où le
/// versement s'effectue réellement. Laisser un vendeur déclarer « c'est payé » sans
/// qu'il puisse le faire serait rouvrir exactement le trou qu'on vient de boucher.
/// </summary>
public sealed record ConfirmReturnRefundCommand(Guid ReturnRequestId, string ProviderReference) : ICommand;

internal abstract class ReturnMutationHandlerBase
{
    protected readonly IReturnRequestRepository Repository;
    protected readonly IReturnsUnitOfWork UnitOfWork;

    protected ReturnMutationHandlerBase(IReturnRequestRepository repository, IReturnsUnitOfWork unitOfWork)
    {
        Repository = repository;
        UnitOfWork = unitOfWork;
    }

    protected async Task<Result> MutateAsync(Guid id, Func<ReturnRequest, Result> mutate, CancellationToken ct)
    {
        var request = await Repository.GetByIdAsync(new ReturnRequestId(id), ct);
        if (request is null)
        {
            return Result.Failure(Error.NotFound("returns.not_found", "Demande de retour introuvable."));
        }

        var result = mutate(request);
        if (result.IsFailure)
        {
            return result;
        }

        await UnitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class ApproveReturnCommandHandler : ReturnMutationHandlerBase, ICommandHandler<ApproveReturnCommand>
{
    public ApproveReturnCommandHandler(IReturnRequestRepository r, IReturnsUnitOfWork u) : base(r, u) { }
    public Task<Result> Handle(ApproveReturnCommand c, CancellationToken ct) => MutateAsync(c.ReturnRequestId, r => r.Approve(), ct);
}

internal sealed class RejectReturnCommandHandler : ReturnMutationHandlerBase, ICommandHandler<RejectReturnCommand>
{
    public RejectReturnCommandHandler(IReturnRequestRepository r, IReturnsUnitOfWork u) : base(r, u) { }
    public Task<Result> Handle(RejectReturnCommand c, CancellationToken ct)
        => MutateAsync(c.ReturnRequestId, r => r.Reject(string.IsNullOrWhiteSpace(c.Reason) ? "Refusé." : c.Reason), ct);
}

internal sealed class AddReturnTrackingCommandHandler : ReturnMutationHandlerBase, ICommandHandler<AddReturnTrackingCommand>
{
    public AddReturnTrackingCommandHandler(IReturnRequestRepository r, IReturnsUnitOfWork u) : base(r, u) { }
    public Task<Result> Handle(AddReturnTrackingCommand c, CancellationToken ct)
        => MutateAsync(c.ReturnRequestId, r => r.AddReturnTracking(c.Carrier, c.TrackingNumber), ct);
}

internal sealed class MarkReturnReceivedCommandHandler : ReturnMutationHandlerBase, ICommandHandler<MarkReturnReceivedCommand>
{
    public MarkReturnReceivedCommandHandler(IReturnRequestRepository r, IReturnsUnitOfWork u) : base(r, u) { }
    public Task<Result> Handle(MarkReturnReceivedCommand c, CancellationToken ct) => MutateAsync(c.ReturnRequestId, r => r.MarkReceived(), ct);
}

/// <summary>
/// Valide le remboursement d'un retour, sous le PLAFOND COMMUN aux deux circuits.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE HANDLER N'UTILISE PAS `MutateAsync`, ET C'EST NÉCESSAIRE.
///
/// Le contrôle à poser n'est pas local au retour : il faut connaître le total de la
/// commande, et ce que l'AUTRE circuit — le remboursement direct en Mobile Money — a
/// déjà versé dessus. Deux lectures inter-modules, donc, avant de muter.
///
/// L'agrégat garde son propre plafond, celui de la ligne. Les deux se cumulent : la
/// ligne borne ce que ce retour peut rendre, la commande borne ce que TOUS les
/// remboursements réunis peuvent rendre. Aucun des deux ne remplace l'autre — sans le
/// premier on rembourserait une ligne à 1 000 pour 9 000 sur une commande à 10 000 ;
/// sans le second, dix lignes intégralement rendues plus un versement direct feraient
/// le double du total.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class ApproveReturnRefundCommandHandler : ICommandHandler<ApproveReturnRefundCommand>
{
    private readonly IReturnRequestRepository _repository;
    private readonly IOrderingModuleApi _orders;
    private readonly ISettlementModuleApi _settlement;
    private readonly IReturnsUnitOfWork _unitOfWork;

    public ApproveReturnRefundCommandHandler(
        IReturnRequestRepository repository,
        IOrderingModuleApi orders,
        ISettlementModuleApi settlement,
        IReturnsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _orders = orders;
        _settlement = settlement;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ApproveReturnRefundCommand c, CancellationToken ct)
    {
        var request = await _repository.GetByIdAsync(new ReturnRequestId(c.ReturnRequestId), ct);
        if (request is null)
        {
            return Result.Failure(Error.NotFound("returns.not_found", "Demande de retour introuvable."));
        }

        var order = await _orders.GetOrderAsync(request.OrderId, ct);
        if (order is null)
        {
            // La commande a disparu sous le retour : on ne peut plus borner quoi que ce
            // soit. Refuser est le seul choix sûr — un plafond qu'on ne sait pas calculer
            // ne doit jamais valoir « pas de plafond ».
            return Result.Failure(Error.NotFound("returns.order.not_found", "Commande introuvable."));
        }

        // Ce que les DEUX circuits ont déjà engagé sur cette commande. Le retour courant
        // n'est pas encore compté : il n'est pas en `RefundPending`, c'est précisément ce
        // qu'on s'apprête à décider.
        var byReturns = await _repository.SumCommittedRefundsForOrderAsync(request.OrderId, ct);
        var byDirect = await _settlement.SumDirectRefundsForOrderAsync(request.OrderId, ct);

        var remainingOnOrder = order.GrandTotal - byReturns - byDirect;
        if (c.Amount > remainingOnOrder)
        {
            return Result.Failure(Error.Validation(
                "returns.amount_exceeds_order",
                $"Le remboursement dépasse ce qui reste remboursable sur la commande "
                + $"({remainingOnOrder:0.##} {order.Currency}), remboursements directs compris."));
        }

        var result = request.ApproveRefund(c.Amount);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class ConfirmReturnRefundCommandHandler : ReturnMutationHandlerBase, ICommandHandler<ConfirmReturnRefundCommand>
{
    public ConfirmReturnRefundCommandHandler(IReturnRequestRepository r, IReturnsUnitOfWork u) : base(r, u) { }

    public Task<Result> Handle(ConfirmReturnRefundCommand c, CancellationToken ct)
        => MutateAsync(c.ReturnRequestId, r => r.ConfirmRefundExecuted(c.ProviderReference, DateTime.UtcNow), ct);
}
