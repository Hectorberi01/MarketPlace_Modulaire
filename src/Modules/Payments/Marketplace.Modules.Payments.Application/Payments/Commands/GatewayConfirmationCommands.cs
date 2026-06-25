using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Payments.Application.Abstractions;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;
using Marketplace.Modules.Payments.Domain.Payments;

namespace Marketplace.Modules.Payments.Application.Payments.Commands;

/// <summary>
/// Traite un webhook PSP : vérifie la signature, normalise l'événement et
/// applique le résultat au paiement corrélé (encaissement / échec / remboursement).
/// Idempotent : un événement déjà appliqué est acquitté sans erreur.
/// </summary>
public sealed record ProcessGatewayWebhookCommand(string Provider, string RawBody, string? Signature) : ICommand;

/// <summary>
/// Confirme un paiement au retour de la redirection : interroge le PSP pour
/// connaître le statut réel de la session/intention, puis l'applique. Sécurise
/// le cas où le webhook n'est pas (encore) arrivé.
/// </summary>
public sealed record ConfirmPaymentFromRedirectCommand(Guid PaymentId) : ICommand;

/// <summary>Applique un résultat PSP normalisé à un paiement, de façon idempotente.</summary>
internal static class GatewayOutcomeApplier
{
    public static Result Apply(Payment payment, GatewayEvent gatewayEvent)
    {
        var providerReference = gatewayEvent.ProviderReference ?? payment.ProviderReference ?? string.Empty;

        return gatewayEvent.Outcome switch
        {
            GatewayOutcome.Captured => payment.Status == PaymentStatus.Captured ? Result.Success() : payment.Capture(providerReference),
            GatewayOutcome.Failed => payment.Status == PaymentStatus.Failed ? Result.Success() : payment.Fail(gatewayEvent.FailureReason ?? "Paiement refusé par le prestataire."),
            GatewayOutcome.Refunded => payment.Status == PaymentStatus.Refunded ? Result.Success() : payment.Refund(),
            _ => Result.Success()
        };
    }
}

internal sealed class ProcessGatewayWebhookCommandHandler : ICommandHandler<ProcessGatewayWebhookCommand>
{
    private readonly IPaymentGatewayResolver _gatewayResolver;
    private readonly IPaymentRepository _repository;
    private readonly IPaymentsUnitOfWork _unitOfWork;

    public ProcessGatewayWebhookCommandHandler(
        IPaymentGatewayResolver gatewayResolver, IPaymentRepository repository, IPaymentsUnitOfWork unitOfWork)
    {
        _gatewayResolver = gatewayResolver;
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ProcessGatewayWebhookCommand command, CancellationToken cancellationToken)
    {
        var gatewayResult = _gatewayResolver.Resolve(command.Provider);
        if (gatewayResult.IsFailure)
        {
            return Result.Failure(gatewayResult.Error);
        }

        var gatewayEvent = await gatewayResult.Value.ParseWebhookAsync(command.RawBody, command.Signature, cancellationToken);
        if (!gatewayEvent.Verified)
        {
            return Result.Failure(Error.Unauthorized("payments.webhook_invalid_signature", "Signature de webhook invalide."));
        }

        if (gatewayEvent.Outcome == GatewayOutcome.Ignored || string.IsNullOrWhiteSpace(gatewayEvent.ProviderReference))
        {
            return Result.Success();
        }

        var payment = await _repository.GetByProviderReferenceAsync(gatewayEvent.ProviderReference!, cancellationToken);
        if (payment is null)
        {
            // Paiement inconnu : on acquitte pour éviter les renvois en boucle du PSP.
            return Result.Success();
        }

        var apply = GatewayOutcomeApplier.Apply(payment, gatewayEvent);
        if (apply.IsFailure)
        {
            return apply;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class ConfirmPaymentFromRedirectCommandHandler : ICommandHandler<ConfirmPaymentFromRedirectCommand>
{
    private readonly IPaymentGatewayResolver _gatewayResolver;
    private readonly IPaymentRepository _repository;
    private readonly IPaymentsUnitOfWork _unitOfWork;

    public ConfirmPaymentFromRedirectCommandHandler(
        IPaymentGatewayResolver gatewayResolver, IPaymentRepository repository, IPaymentsUnitOfWork unitOfWork)
    {
        _gatewayResolver = gatewayResolver;
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ConfirmPaymentFromRedirectCommand command, CancellationToken cancellationToken)
    {
        var payment = await _repository.GetByIdAsync(new PaymentId(command.PaymentId), cancellationToken);
        if (payment is null)
        {
            return Result.Failure(Error.NotFound("payments.not_found", "Paiement introuvable."));
        }

        if (payment.Status == PaymentStatus.Captured)
        {
            return Result.Success();
        }

        if (string.IsNullOrWhiteSpace(payment.ProviderReference))
        {
            return Result.Failure(Error.Conflict("payments.no_session", "Aucune session PSP rattachée à ce paiement."));
        }

        var gatewayResult = _gatewayResolver.Resolve(payment.Provider);
        if (gatewayResult.IsFailure)
        {
            return Result.Failure(gatewayResult.Error);
        }

        var gatewayEvent = await gatewayResult.Value.GetStatusAsync(payment.ProviderReference, cancellationToken);

        var apply = GatewayOutcomeApplier.Apply(payment, gatewayEvent);
        if (apply.IsFailure)
        {
            return apply;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
