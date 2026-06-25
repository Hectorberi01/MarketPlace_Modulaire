using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Payments.Application.Abstractions;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;
using Marketplace.Modules.Payments.Domain.Payments;

namespace Marketplace.Modules.Payments.Application.Payments.Commands.InitiatePayment;

/// <summary>
/// Initie un paiement : lit le montant de la commande via Ordering (Contracts),
/// refuse si la commande n'attend pas de paiement ou si un paiement est déjà en
/// cours, crée le paiement (Pending), puis ouvre la session auprès du PSP
/// (checkout hébergé ou intention) et rattache sa référence pour la corrélation.
/// </summary>
internal sealed class InitiatePaymentCommandHandler : ICommandHandler<InitiatePaymentCommand, InitiatePaymentResult>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IOrderingModuleApi _orderingModuleApi;
    private readonly IPaymentGatewayResolver _gatewayResolver;
    private readonly IPaymentsUnitOfWork _unitOfWork;

    public InitiatePaymentCommandHandler(
        IPaymentRepository paymentRepository,
        IOrderingModuleApi orderingModuleApi,
        IPaymentGatewayResolver gatewayResolver,
        IPaymentsUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _orderingModuleApi = orderingModuleApi;
        _gatewayResolver = gatewayResolver;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<InitiatePaymentResult>> Handle(InitiatePaymentCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<PaymentMethod>(command.Method, ignoreCase: true, out var method))
        {
            return Result.Failure<InitiatePaymentResult>(Error.Validation("payments.method_invalid", "Moyen de paiement inconnu."));
        }

        if (!Enum.TryParse<PaymentFlow>(command.Flow, ignoreCase: true, out var flow))
        {
            return Result.Failure<InitiatePaymentResult>(Error.Validation("payments.flow_invalid", "Flux de paiement inconnu (HostedCheckout / PaymentIntent)."));
        }

        var gatewayResult = _gatewayResolver.Resolve(command.Provider);
        if (gatewayResult.IsFailure)
        {
            return Result.Failure<InitiatePaymentResult>(gatewayResult.Error);
        }

        if (gatewayResult.Value.RequiresPayerPhone && string.IsNullOrWhiteSpace(command.PayerPhone))
        {
            return Result.Failure<InitiatePaymentResult>(
                Error.Validation("payments.payer_phone_required", "Le numéro du payeur est requis pour le Mobile Money."));
        }

        var order = await _orderingModuleApi.GetOrderAsync(command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<InitiatePaymentResult>(Error.NotFound("payments.order.not_found", "Commande introuvable."));
        }

        if (!string.Equals(order.Status, "AwaitingPayment", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<InitiatePaymentResult>(Error.Conflict("payments.order.not_payable", "La commande n'attend pas de paiement."));
        }

        var existing = await _paymentRepository.GetByOrderAsync(command.OrderId, cancellationToken);
        if (existing is not null && existing.Status is PaymentStatus.Pending or PaymentStatus.Authorized or PaymentStatus.Captured)
        {
            return Result.Failure<InitiatePaymentResult>(Error.Conflict("payments.already_exists", "Un paiement est déjà en cours pour cette commande."));
        }

        var amountResult = Money.Create(order.GrandTotal, order.Currency);
        if (amountResult.IsFailure)
        {
            return Result.Failure<InitiatePaymentResult>(amountResult.Error);
        }

        var paymentResult = Payment.Create(order.Id, order.BuyerId, amountResult.Value, method, gatewayResult.Value.Provider, flow);
        if (paymentResult.IsFailure)
        {
            return Result.Failure<InitiatePaymentResult>(paymentResult.Error);
        }

        var payment = paymentResult.Value;
        var gateway = gatewayResult.Value;

        var context = new GatewayChargeContext(
            payment.Id.Value, payment.OrderId, payment.Amount.Amount, payment.Amount.Currency,
            command.ReturnUrl, command.CancelUrl, command.PayerPhone);

        var session = flow == PaymentFlow.HostedCheckout
            ? await gateway.CreateCheckoutAsync(context, cancellationToken)
            : await gateway.CreatePaymentIntentAsync(context, cancellationToken);

        var attach = payment.AttachGatewaySession(session.ProviderReference);
        if (attach.IsFailure)
        {
            return Result.Failure<InitiatePaymentResult>(attach.Error);
        }

        await _paymentRepository.AddAsync(payment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new InitiatePaymentResult(
            payment.Id.Value, gateway.Provider, flow.ToString(), session.ProviderReference, session.RedirectUrl, session.ClientSecret);
    }
}
