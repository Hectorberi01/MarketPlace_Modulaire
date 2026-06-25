using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Disputes.Application.Abstractions;
using Marketplace.Modules.Disputes.Domain.Disputes;
using Marketplace.Modules.Ordering.Contracts;

namespace Marketplace.Modules.Disputes.Application.Disputes;

/// <summary>Ouvre un litige sur une commande (par l'acheteur ou un vendeur concerné).</summary>
public sealed record OpenDisputeCommand(Guid RaisedBy, Guid OrderId, string Type, string Message, string? PhotoUrl) : ICommand<Guid>;

public sealed class OpenDisputeCommandValidator : AbstractValidator<OpenDisputeCommand>
{
    public OpenDisputeCommandValidator()
    {
        RuleFor(c => c.RaisedBy).NotEmpty();
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Type).Must(v => Enum.TryParse<DisputeType>(v, true, out _)).WithMessage("Type de litige invalide.");
        RuleFor(c => c.Message).NotEmpty().MaximumLength(2000);
    }
}

internal sealed class OpenDisputeCommandHandler : ICommandHandler<OpenDisputeCommand, Guid>
{
    private readonly IDisputeRepository _repository;
    private readonly IOrderingModuleApi _orderingModuleApi;
    private readonly IDisputesUnitOfWork _unitOfWork;

    public OpenDisputeCommandHandler(IDisputeRepository repository, IOrderingModuleApi orderingModuleApi, IDisputesUnitOfWork unitOfWork)
    {
        _repository = repository;
        _orderingModuleApi = orderingModuleApi;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(OpenDisputeCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<DisputeType>(command.Type, ignoreCase: true, out var type))
        {
            return Result.Failure<Guid>(Error.Validation("disputes.type_invalid", "Type de litige inconnu."));
        }

        var order = await _orderingModuleApi.GetOrderAsync(command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<Guid>(Error.NotFound("disputes.order.not_found", "Commande introuvable."));
        }

        var isParty = order.BuyerId == command.RaisedBy || order.Lines.Any(l => l.SellerId == command.RaisedBy);
        if (!isParty)
        {
            return Result.Failure<Guid>(Error.Forbidden("disputes.not_party", "Vous n'êtes pas partie à cette commande."));
        }

        var result = Dispute.Open(order.Id, command.RaisedBy, type, command.Message, command.PhotoUrl);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _repository.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result.Value.Id.Value;
    }
}
