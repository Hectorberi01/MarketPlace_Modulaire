using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Queries.GetUser;

internal sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, UserSummary>
{
    private readonly IUserRepository _userRepository;

    public GetUserQueryHandler(IUserRepository userRepository)
        => _userRepository = userRepository;

    public async Task<Result<UserSummary>> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(query.UserId), cancellationToken);
        if (user is null)
        {
            return Error.NotFound("identity.user.not_found", $"Compte {query.UserId} introuvable.");
        }

        return new UserSummary(
            user.Id.Value,
            user.FirstName,
            user.LastName,
            user.Email.Value,
            user.PhoneNumber.Value,
            user.Status.ToString(),
            user.EmailVerified,
            user.MfaEnabled,
            user.RoleIds.ToList(),
            user.AcceptedTermsVersion,
            user.AcceptedTermsOnUtc,
            user.EmailVerifiedByAdminOnUtc);
    }
}
