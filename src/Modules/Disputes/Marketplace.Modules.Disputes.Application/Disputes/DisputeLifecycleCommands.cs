using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Disputes.Application.Abstractions;
using Marketplace.Modules.Disputes.Domain.Disputes;

namespace Marketplace.Modules.Disputes.Application.Disputes;

/// <summary>Ajoute un message (et preuve) à un litige.</summary>
public sealed record AddDisputeMessageCommand(Guid DisputeId, Guid AuthorId, string Body, string? PhotoUrl) : ICommand;

/// <summary>Passe le litige en examen (modérateur).</summary>
public sealed record StartDisputeReviewCommand(Guid DisputeId) : ICommand;

/// <summary>Tranche le litige (RefundBuyer / ReleaseToSeller / PartialRefund).</summary>
public sealed record ResolveDisputeCommand(Guid DisputeId, string Resolution) : ICommand;

/// <summary>Escalade le litige.</summary>
public sealed record EscalateDisputeCommand(Guid DisputeId) : ICommand;

internal abstract class DisputeMutationHandlerBase
{
    protected readonly IDisputeRepository Repository;
    protected readonly IDisputesUnitOfWork UnitOfWork;

    protected DisputeMutationHandlerBase(IDisputeRepository repository, IDisputesUnitOfWork unitOfWork)
    {
        Repository = repository;
        UnitOfWork = unitOfWork;
    }

    protected async Task<Result> MutateAsync(Guid id, Func<Dispute, Result> mutate, CancellationToken ct)
    {
        var dispute = await Repository.GetByIdAsync(new DisputeId(id), ct);
        if (dispute is null)
        {
            return Result.Failure(Error.NotFound("disputes.not_found", "Litige introuvable."));
        }

        var result = mutate(dispute);
        if (result.IsFailure)
        {
            return result;
        }

        await UnitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class AddDisputeMessageCommandHandler : DisputeMutationHandlerBase, ICommandHandler<AddDisputeMessageCommand>
{
    public AddDisputeMessageCommandHandler(IDisputeRepository r, IDisputesUnitOfWork u) : base(r, u) { }
    public Task<Result> Handle(AddDisputeMessageCommand c, CancellationToken ct)
        => MutateAsync(c.DisputeId, d => d.AddMessage(c.AuthorId, c.Body, c.PhotoUrl), ct);
}

internal sealed class StartDisputeReviewCommandHandler : DisputeMutationHandlerBase, ICommandHandler<StartDisputeReviewCommand>
{
    public StartDisputeReviewCommandHandler(IDisputeRepository r, IDisputesUnitOfWork u) : base(r, u) { }
    public Task<Result> Handle(StartDisputeReviewCommand c, CancellationToken ct) => MutateAsync(c.DisputeId, d => d.StartReview(), ct);
}

internal sealed class ResolveDisputeCommandHandler : DisputeMutationHandlerBase, ICommandHandler<ResolveDisputeCommand>
{
    public ResolveDisputeCommandHandler(IDisputeRepository r, IDisputesUnitOfWork u) : base(r, u) { }

    public Task<Result> Handle(ResolveDisputeCommand c, CancellationToken ct)
    {
        if (!Enum.TryParse<DisputeResolution>(c.Resolution, ignoreCase: true, out var resolution))
        {
            return Task.FromResult(Result.Failure(Error.Validation("disputes.resolution_invalid", "Décision inconnue.")));
        }

        return MutateAsync(c.DisputeId, d => d.Resolve(resolution), ct);
    }
}

internal sealed class EscalateDisputeCommandHandler : DisputeMutationHandlerBase, ICommandHandler<EscalateDisputeCommand>
{
    public EscalateDisputeCommandHandler(IDisputeRepository r, IDisputesUnitOfWork u) : base(r, u) { }
    public Task<Result> Handle(EscalateDisputeCommand c, CancellationToken ct) => MutateAsync(c.DisputeId, d => d.Escalate(), ct);
}
