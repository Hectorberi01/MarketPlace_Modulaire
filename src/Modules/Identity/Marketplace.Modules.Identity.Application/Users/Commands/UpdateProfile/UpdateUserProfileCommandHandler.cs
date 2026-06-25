using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.UpdateProfile;

/// <summary>Valide le téléphone et applique la mise à jour de profil sur l'agrégat.</summary>
internal sealed class UpdateUserProfileCommandHandler : ICommandHandler<UpdateUserProfileCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public UpdateUserProfileCommandHandler(IUserRepository userRepository, IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateUserProfileCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        var phoneResult = PhoneNumber.Create(command.PhoneNumber);
        if (phoneResult.IsFailure)
        {
            return Result.Failure(phoneResult.Error);
        }

        var result = user.UpdateProfile(command.FirstName, command.LastName, phoneResult.Value);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
