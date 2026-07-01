using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Domain.PaymentMethods;

namespace Marketplace.Modules.Identity.Application.PaymentMethods;

/// <summary>Liste les moyens de paiement d'un utilisateur (défaut en tête).</summary>
public sealed record ListPaymentMethodsQuery(Guid UserId) : IQuery<IReadOnlyList<PaymentMethodDto>>;

internal sealed class ListPaymentMethodsQueryHandler
    : IQueryHandler<ListPaymentMethodsQuery, IReadOnlyList<PaymentMethodDto>>
{
    private readonly IPaymentMethodRepository _repository;

    public ListPaymentMethodsQueryHandler(IPaymentMethodRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<PaymentMethodDto>>> Handle(
        ListPaymentMethodsQuery query, CancellationToken cancellationToken)
    {
        var methods = await _repository.ListByUserAsync(query.UserId, cancellationToken);
        IReadOnlyList<PaymentMethodDto> result = methods.Select(Map).ToList();
        return Result.Success(result);
    }

    internal static PaymentMethodDto Map(PaymentMethod m) => new(
        m.Id.Value,
        m.Type.ToString(),
        m.Label,
        m.Provider,
        m.Type == PaymentMethodType.Card ? $"•••• {m.AccountRef}" : m.AccountRef,
        m.ExpiryMonth,
        m.ExpiryYear,
        m.HolderName,
        m.IsDefault);
}
