using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.Addresses;

namespace Marketplace.Modules.Identity.Application.Addresses;

/// <summary>Supprime une adresse du carnet (vérifie la propriété).</summary>
public sealed record DeleteAddressCommand(Guid UserId, Guid AddressId) : ICommand;

internal sealed class DeleteAddressCommandHandler : ICommandHandler<DeleteAddressCommand>
{
    private readonly IAddressRepository _repository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public DeleteAddressCommandHandler(IAddressRepository repository, IIdentityUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteAddressCommand command, CancellationToken cancellationToken)
    {
        var address = await _repository.GetByIdAsync(new AddressId(command.AddressId), cancellationToken);
        if (address is null || address.UserId != command.UserId)
        {
            return Result.Failure(Error.NotFound("identity.address.not_found", "Adresse introuvable."));
        }

        _repository.Remove(address);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
