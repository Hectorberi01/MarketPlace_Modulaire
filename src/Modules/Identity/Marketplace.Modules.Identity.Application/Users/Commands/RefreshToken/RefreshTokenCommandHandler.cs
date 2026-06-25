using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Application.Models;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.RefreshToken;

/// <summary>
/// Valide le refresh token (par son hash), le révoque (rotation) et émet une
/// nouvelle paire. Un token inconnu, expiré ou révoqué renvoie 401.
/// </summary>
internal sealed class RefreshTokenCommandHandler : ICommandHandler<RefreshTokenCommand, AuthTokens>
{
    private static readonly Error InvalidToken =
        Error.Unauthorized("identity.auth.refresh_invalid", "Refresh token invalide ou expiré.");

    private readonly IUserRepository _userRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly AuthTokenIssuer _tokenIssuer;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public RefreshTokenCommandHandler(
        IUserRepository userRepository,
        ISecureTokenGenerator tokenGenerator,
        AuthTokenIssuer tokenIssuer,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _tokenIssuer = tokenIssuer;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AuthTokens>> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var tokenHash = _tokenGenerator.Hash(command.RefreshToken);

        var user = await _userRepository.GetByRefreshTokenHashAsync(tokenHash, cancellationToken);
        if (user is null)
        {
            return Result.Failure<AuthTokens>(InvalidToken);
        }

        var existing = user.FindActiveRefreshToken(tokenHash, DateTime.UtcNow);
        if (existing is null)
        {
            return Result.Failure<AuthTokens>(InvalidToken);
        }

        if (user.Status == UserStatus.Suspended)
        {
            return Result.Failure<AuthTokens>(Error.Forbidden("identity.auth.suspended", "Ce compte est suspendu."));
        }

        // Rotation : on révoque l'ancien et on en émet un nouveau.
        user.RevokeRefreshToken(tokenHash);
        var tokens = await _tokenIssuer.IssueAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return tokens;
    }
}
