using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Queries.ListUsers;

/// <summary>Liste tous les comptes de la plateforme (back-office admin).</summary>
public sealed record ListUsersQuery : IQuery<IReadOnlyList<UserSummary>>;

internal sealed class ListUsersQueryHandler : IQueryHandler<ListUsersQuery, IReadOnlyList<UserSummary>>
{
    private readonly IUserRepository _userRepository;

    public ListUsersQueryHandler(IUserRepository userRepository) => _userRepository = userRepository;

    public async Task<Result<IReadOnlyList<UserSummary>>> Handle(ListUsersQuery query, CancellationToken cancellationToken)
    {
        var users = await _userRepository.ListAllAsync(cancellationToken: cancellationToken);
        IReadOnlyList<UserSummary> summaries = users
            .Select(u => new UserSummary(
                u.Id.Value, u.FirstName, u.LastName, u.Email.Value, u.PhoneNumber.Value,
                u.Status.ToString(), u.EmailVerified, u.MfaEnabled, u.RoleIds.ToList()))
            .ToList();
        return Result.Success(summaries);
    }
}
