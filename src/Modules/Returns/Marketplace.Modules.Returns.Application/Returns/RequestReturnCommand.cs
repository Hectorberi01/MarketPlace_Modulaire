using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Returns.Application.Abstractions;
using Marketplace.Modules.Returns.Domain.Returns;

namespace Marketplace.Modules.Returns.Application.Returns;

/// <summary>Ouvre une demande de retour pour une ligne d'une commande de l'acheteur.</summary>
public sealed record RequestReturnCommand(Guid BuyerId, Guid OrderId, Guid OfferId, string Reason) : ICommand<Guid>;

public sealed class RequestReturnCommandValidator : AbstractValidator<RequestReturnCommand>
{
    public RequestReturnCommandValidator()
    {
        RuleFor(c => c.BuyerId).NotEmpty();
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.OfferId).NotEmpty();
        RuleFor(c => c.Reason).Must(v => Enum.TryParse<ReturnReason>(v, true, out _)).WithMessage("Motif de retour invalide.");
    }
}

internal sealed class RequestReturnCommandHandler : ICommandHandler<RequestReturnCommand, Guid>
{
    private readonly IReturnRequestRepository _repository;
    private readonly IOrderingModuleApi _orderingModuleApi;
    private readonly IReturnsUnitOfWork _unitOfWork;

    public RequestReturnCommandHandler(
        IReturnRequestRepository repository, IOrderingModuleApi orderingModuleApi, IReturnsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _orderingModuleApi = orderingModuleApi;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(RequestReturnCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ReturnReason>(command.Reason, ignoreCase: true, out var reason))
        {
            return Result.Failure<Guid>(Error.Validation("returns.reason_invalid", "Motif de retour inconnu."));
        }

        var order = await _orderingModuleApi.GetOrderAsync(command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<Guid>(Error.NotFound("returns.order.not_found", "Commande introuvable."));
        }

        if (order.BuyerId != command.BuyerId)
        {
            return Result.Failure<Guid>(Error.Forbidden("returns.not_owner", "Cette commande n'appartient pas à l'acheteur."));
        }

        if (!string.Equals(order.Status, "Confirmed", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<Guid>(Error.Conflict("returns.order_not_eligible", "Seule une commande confirmée peut faire l'objet d'un retour."));
        }

        var line = order.Lines.FirstOrDefault(l => l.OfferId == command.OfferId);
        if (line is null)
        {
            return Result.Failure<Guid>(Error.Conflict("returns.line_not_found", "Cette ligne n'est pas dans la commande."));
        }

        if (await _repository.ExistsOpenAsync(command.OrderId, command.OfferId, cancellationToken))
        {
            return Result.Failure<Guid>(Error.Conflict("returns.already_open", "Un retour est déjà ouvert pour cette ligne."));
        }

        var result = ReturnRequest.Create(order.Id, command.OfferId, command.BuyerId, line.SellerId, reason, order.Currency);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _repository.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result.Value.Id.Value;
    }
}
