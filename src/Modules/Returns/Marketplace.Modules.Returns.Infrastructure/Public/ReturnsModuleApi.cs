using Marketplace.Modules.Returns.Contracts;
using Marketplace.Modules.Returns.Domain.Returns;

namespace Marketplace.Modules.Returns.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique de Returns. Lecture seule : ce module
/// n'a rien à faire faire par un autre, il a seulement quelque chose à lui dire.
/// </summary>
internal sealed class ReturnsModuleApi : IReturnsModuleApi
{
    private readonly IReturnRequestRepository _repository;

    public ReturnsModuleApi(IReturnRequestRepository repository) => _repository = repository;

    public Task<decimal> SumCommittedRefundsForOrderAsync(
        Guid orderId, CancellationToken cancellationToken = default)
        => _repository.SumCommittedRefundsForOrderAsync(orderId, cancellationToken);
}
