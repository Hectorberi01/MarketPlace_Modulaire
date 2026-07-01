using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.PasswordReset;

internal sealed class RequestPasswordResetCommandHandler : ICommandHandler<RequestPasswordResetCommand, string?>
{
    private readonly IUserRepository _userRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public RequestPasswordResetCommandHandler(
        IUserRepository userRepository,
        ISecureTokenGenerator tokenGenerator,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<string?>> Handle(RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            // Pas d'énumération de comptes : on renvoie un succès silencieux.
            return Result.Success<string?>(null);
        }

        var (raw, hash) = _tokenGenerator.Generate();
        var begin = user.BeginPasswordReset(hash, DateTime.UtcNow.AddHours(1));
        if (begin.IsFailure)
        {
            return Result.Failure<string?>(begin.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success<string?>(raw);
    }
}
