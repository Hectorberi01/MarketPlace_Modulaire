using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.Addresses;

namespace Marketplace.Modules.Identity.Application.Addresses;

/// <summary>Définit l'adresse par défaut (et retire le défaut des autres).</summary>
public sealed record SetDefaultAddressCommand(Guid UserId, Guid AddressId) : ICommand;

internal sealed class SetDefaultAddressCommandHandler : ICommandHandler<SetDefaultAddressCommand>
{
    private readonly IAddressRepository _repository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public SetDefaultAddressCommandHandler(IAddressRepository repository, IIdentityUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SetDefaultAddressCommand command, CancellationToken cancellationToken)
    {
        var addresses = await _repository.ListByUserAsync(command.UserId, cancellationToken);
        var target = addresses.FirstOrDefault(a => a.Id.Value == command.AddressId);
        if (target is null)
        {
            return Result.Failure(Error.NotFound("identity.address.not_found", "Adresse introuvable."));
        }

        foreach (var address in addresses)
        {
            if (address.IsDefault)
            {
                address.ClearDefault();
            }
        }

        target.MarkDefault();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
