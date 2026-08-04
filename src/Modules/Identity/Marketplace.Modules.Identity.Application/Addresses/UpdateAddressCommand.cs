using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.Addresses;

namespace Marketplace.Modules.Identity.Application.Addresses;

/// <summary>
/// Met à jour une adresse du carnet (vérifie la propriété via <see cref="UserId"/>).
/// Si <see cref="MakeDefault"/> est vrai, l'adresse devient le nouveau défaut.
/// </summary>
public sealed record UpdateAddressCommand(
    Guid UserId,
    Guid AddressId,
    string? Label,
    string? Recipient,
    string? Phone,
    string? CommuneCode,
    string? Quartier,
    string? Landmark,
    string? Line1,
    double? Latitude,
    double? Longitude,
    bool MakeDefault) : ICommand;

internal sealed class UpdateAddressCommandHandler : ICommandHandler<UpdateAddressCommand>
{
    private readonly IAddressRepository _repository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public UpdateAddressCommandHandler(IAddressRepository repository, IIdentityUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateAddressCommand command, CancellationToken cancellationToken)
    {
        var addresses = await _repository.ListByUserAsync(command.UserId, cancellationToken);
        var target = addresses.FirstOrDefault(a => a.Id.Value == command.AddressId);
        if (target is null)
        {
            return Result.Failure(Error.NotFound("identity.address.not_found", "Adresse introuvable."));
        }

        var updated = target.Update(
            command.Label, command.Recipient, command.Phone,
            command.CommuneCode, command.Quartier, command.Landmark, command.Line1,
            command.Latitude, command.Longitude);
        if (updated.IsFailure)
        {
            return updated;
        }

        if (command.MakeDefault && !target.IsDefault)
        {
            foreach (var other in addresses.Where(a => a.IsDefault))
            {
                other.ClearDefault();
            }

            target.MarkDefault();
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
