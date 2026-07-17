using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Application.Pagination;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Queries.ListUsers;

/// <summary>Page de comptes pour la console admin (recherche prénom/nom, filtre statut).</summary>
public sealed record ListUsersQuery(
    int Page = 1,
    int PageSize = PageRequest.DefaultPageSize,
    string? Search = null,
    string? Status = null,
    string? Sort = null,
    string? Dir = null) : IQuery<PagedResult<UserSummary>>;

internal sealed class ListUsersQueryHandler : IQueryHandler<ListUsersQuery, PagedResult<UserSummary>>
{
    private readonly IUserRepository _userRepository;

    public ListUsersQueryHandler(IUserRepository userRepository) => _userRepository = userRepository;

    public async Task<Result<PagedResult<UserSummary>>> Handle(ListUsersQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = PageRequest.Normalize(query.Page, query.PageSize);
        UserStatus? status = Enum.TryParse<UserStatus>(query.Status, ignoreCase: true, out var parsed) ? parsed : null;
        bool desc = !string.Equals(query.Dir, "asc", StringComparison.OrdinalIgnoreCase);

        var (users, total, statusCounts) = await _userRepository.ListPagedAsync(page, pageSize, query.Search, status, query.Sort, desc, cancellationToken);

        var items = users
            .Select(u => new UserSummary(
                u.Id.Value, u.FirstName, u.LastName, u.Email.Value, u.PhoneNumber.Value,
                u.Status.ToString(), u.EmailVerified, u.MfaEnabled, u.RoleIds.ToList(),
                u.AcceptedTermsVersion, u.AcceptedTermsOnUtc, u.EmailVerifiedByAdminOnUtc))
            .ToList();

        return Result.Success(new PagedResult<UserSummary>(items, total, page, pageSize, statusCounts));
    }
}
