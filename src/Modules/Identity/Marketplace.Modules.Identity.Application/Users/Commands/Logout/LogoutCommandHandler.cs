using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.Logout;

/// <summary>Révoque le refresh token fourni s'il appartient à l'utilisateur.</summary>
internal sealed class LogoutCommandHandler : ICommandHandler<LogoutCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public LogoutCommandHandler(
        IUserRepository userRepository,
        ISecureTokenGenerator tokenGenerator,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        user.RevokeRefreshToken(_tokenGenerator.Hash(command.RefreshToken));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
