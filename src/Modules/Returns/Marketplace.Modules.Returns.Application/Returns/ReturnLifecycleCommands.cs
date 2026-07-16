using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Returns.Application.Abstractions;
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

internal sealed class ApproveReturnRefundCommandHandler : ReturnMutationHandlerBase, ICommandHandler<ApproveReturnRefundCommand>
{
    public ApproveReturnRefundCommandHandler(IReturnRequestRepository r, IReturnsUnitOfWork u) : base(r, u) { }

    public Task<Result> Handle(ApproveReturnRefundCommand c, CancellationToken ct)
        => MutateAsync(c.ReturnRequestId, r => r.ApproveRefund(c.Amount), ct);
}

internal sealed class ConfirmReturnRefundCommandHandler : ReturnMutationHandlerBase, ICommandHandler<ConfirmReturnRefundCommand>
{
    public ConfirmReturnRefundCommandHandler(IReturnRequestRepository r, IReturnsUnitOfWork u) : base(r, u) { }

    public Task<Result> Handle(ConfirmReturnRefundCommand c, CancellationToken ct)
        => MutateAsync(c.ReturnRequestId, r => r.ConfirmRefundExecuted(c.ProviderReference, DateTime.UtcNow), ct);
}
