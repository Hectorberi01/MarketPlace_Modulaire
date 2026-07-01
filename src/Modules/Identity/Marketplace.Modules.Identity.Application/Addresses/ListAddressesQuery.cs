using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Domain.Addresses;

namespace Marketplace.Modules.Identity.Application.Addresses;

/// <summary>Liste les adresses d'un utilisateur (défaut en tête).</summary>
public sealed record ListAddressesQuery(Guid UserId) : IQuery<IReadOnlyList<AddressDto>>;

internal sealed class ListAddressesQueryHandler : IQueryHandler<ListAddressesQuery, IReadOnlyList<AddressDto>>
{
    private readonly IAddressRepository _repository;

    public ListAddressesQueryHandler(IAddressRepository repository) => _repository = repository;

    public async Task<Result<IReadOnlyList<AddressDto>>> Handle(ListAddressesQuery query, CancellationToken cancellationToken)
    {
        var addresses = await _repository.ListByUserAsync(query.UserId, cancellationToken);
        IReadOnlyList<AddressDto> result = addresses
            .Select(a => new AddressDto(a.Id.Value, a.Label, a.Recipient, a.Line1, a.Line2, a.City, a.Country, a.Phone, a.IsDefault))
            .ToList();
        return Result.Success(result);
    }
}
