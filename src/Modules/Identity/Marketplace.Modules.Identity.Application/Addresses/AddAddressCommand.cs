using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.Addresses;

namespace Marketplace.Modules.Identity.Application.Addresses;

/// <summary>Ajoute une adresse au carnet de l'utilisateur.</summary>
public sealed record AddAddressCommand(
    Guid UserId,
    string? Label,
    string? Recipient,
    string Line1,
    string? Line2,
    string City,
    string? Country,
    string? Phone,
    bool MakeDefault) : ICommand<Guid>;

internal sealed class AddAddressCommandHandler : ICommandHandler<AddAddressCommand, Guid>
{
    private readonly IAddressRepository _repository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public AddAddressCommandHandler(IAddressRepository repository, IIdentityUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(AddAddressCommand command, CancellationToken cancellationToken)
    {
        var existing = await _repository.ListByUserAsync(command.UserId, cancellationToken);
        // La première adresse est forcément le défaut.
        var makeDefault = command.MakeDefault || existing.Count == 0;

        var created = Address.Create(
            command.UserId, command.Label, command.Recipient, command.Line1, command.Line2,
            command.City, command.Country, command.Phone, makeDefault);
        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        if (makeDefault)
        {
            foreach (var other in existing.Where(a => a.IsDefault))
            {
                other.ClearDefault();
            }
        }

        await _repository.AddAsync(created.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(created.Value.Id.Value);
    }
}
