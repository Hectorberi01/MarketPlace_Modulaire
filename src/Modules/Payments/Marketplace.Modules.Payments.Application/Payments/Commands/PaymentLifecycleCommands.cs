using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Payments.Application.Abstractions;
using Marketplace.Modules.Payments.Domain.Payments;

namespace Marketplace.Modules.Payments.Application.Payments.Commands;

/// <summary>Encaisse un paiement (simule la confirmation du PSP). Émet PaymentCaptured.</summary>
public sealed record CapturePaymentCommand(Guid PaymentId, string ProviderReference) : ICommand;

/// <summary>Marque un paiement en échec. Émet PaymentFailed.</summary>
public sealed record FailPaymentCommand(Guid PaymentId, string Reason) : ICommand;

/// <summary>Rembourse un paiement encaissé. Émet PaymentRefunded.</summary>
public sealed record RefundPaymentCommand(Guid PaymentId) : ICommand;

internal sealed class CapturePaymentCommandHandler : ICommandHandler<CapturePaymentCommand>
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentsUnitOfWork _unitOfWork;

    public CapturePaymentCommandHandler(IPaymentRepository repository, IPaymentsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(CapturePaymentCommand command, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetByIdAsync(new PaymentId(command.PaymentId), cancellationToken);
        if (payment is null)
        {
            return Result.Failure(Error.NotFound("payments.not_found", "Paiement introuvable."));
        }

        var result = payment.Capture(command.ProviderReference);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class FailPaymentCommandHandler : ICommandHandler<FailPaymentCommand>
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentsUnitOfWork _unitOfWork;

    public FailPaymentCommandHandler(IPaymentRepository repository, IPaymentsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(FailPaymentCommand command, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetByIdAsync(new PaymentId(command.PaymentId), cancellationToken);
        if (payment is null)
        {
            return Result.Failure(Error.NotFound("payments.not_found", "Paiement introuvable."));
        }

        var result = payment.Fail(string.IsNullOrWhiteSpace(command.Reason) ? "Paiement refusé." : command.Reason);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class RefundPaymentCommandHandler : ICommandHandler<RefundPaymentCommand>
{
    private readonly IPaymentRepository _repository;
    private readonly IPaymentsUnitOfWork _unitOfWork;

    public RefundPaymentCommandHandler(IPaymentRepository repository, IPaymentsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RefundPaymentCommand command, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetByIdAsync(new PaymentId(command.PaymentId), cancellationToken);
        if (payment is null)
        {
            return Result.Failure(Error.NotFound("payments.not_found", "Paiement introuvable."));
        }

        var result = payment.Refund();
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
