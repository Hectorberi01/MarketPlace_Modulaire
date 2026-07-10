using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Cart.Contracts;
using Marketplace.Modules.Identity.Application.Roles.Commands.CreateRole;
using Marketplace.Modules.Identity.Application.Roles.Commands.DeleteRole;
using Marketplace.Modules.Identity.Application.Roles.Commands.SetRolePermissions;
using Marketplace.Modules.Identity.Application.Roles.Commands.UpdateRole;
using Marketplace.Modules.Identity.Application.Roles.Queries.GetRole;
using Marketplace.Modules.Identity.Application.Roles.Queries.ListRoles;
using Marketplace.Modules.Identity.Application.Users.Commands.AssignRole;
using Marketplace.Modules.Identity.Application.Users.Commands.ReactivateUser;
using Marketplace.Modules.Identity.Application.Users.Commands.RemoveRole;
using Marketplace.Modules.Identity.Application.Users.Commands.SuspendUser;
using Marketplace.Modules.Identity.Application.Users.Queries.GetUser;
using Marketplace.Modules.Identity.Application.Users.Queries.ListUsers;
using Marketplace.Modules.Identity.Application.Users.Commands.RegisterUser;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>BFF Admin — comptes & rôles : suspension/réactivation, gestion des rôles et permissions (RBAC).</summary>
public static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapAdminGroup("/admin/users", "Admin · Comptes");
        users.MapGet("/", ListUsersAsync);
        users.MapPost("/register", RegisterUserAsync);
        users.MapGet("/{id:guid}", GetUserAsync);
        users.MapGet("/{id:guid}/cart", GetUserCartAsync);
        users.MapPost("/{id:guid}/suspend", SuspendUserAsync);
        users.MapPost("/{id:guid}/reactivate", ReactivateUserAsync);
        users.MapPost("/{id:guid}/roles", AssignRoleAsync);
        users.MapDelete("/{id:guid}/roles/{roleId:guid}", RemoveRoleAsync);

        var roles = app.MapAdminGroup("/admin/roles", "Admin · Rôles");
        roles.MapGet("/", ListRolesAsync);
        roles.MapGet("/{id:guid}", GetRoleAsync);
        roles.MapPost("/", CreateRoleAsync);
        roles.MapPut("/{id:guid}", UpdateRoleAsync);
        roles.MapPut("/{id:guid}/permissions", SetPermissionsAsync);
        roles.MapDelete("/{id:guid}", DeleteRoleAsync);

        return app;
    }

    /// <summary>Liste de tous les comptes de la plateforme.</summary>
    private static async Task<IResult> ListUsersAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListUsersQuery(), ct)).Match(u => Results.Ok(u));

    /// <summary>Crée un compte (par ex. un administrateur) et lui attribue les rôles fournis.</summary>
    private static async Task<IResult> RegisterUserAsync(RegisterUserRequestBody request, ISender sender, CancellationToken ct)
    {
        var userResult = await sender.Send(new RegisterUserCommand(
            request.FirstName, request.LastName, request.Email, request.PhoneNumber ?? string.Empty, request.Password), ct);
        if (userResult.IsFailure)
        {
            return userResult.Match(_ => Results.Ok()); // mappe l'échec (400/409) proprement
        }

        if (request.RoleIds is { Count: > 0 })
        {
            foreach (var roleId in request.RoleIds)
            {
                await sender.Send(new AssignRoleCommand(userResult.Value, roleId), ct);
            }
        }

        return Results.Ok(new { userId = userResult.Value });
    }

    /// <summary>Détail d'un compte.</summary>
    private static async Task<IResult> GetUserAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetUserQuery(id), ct)).Match(u => Results.Ok(u));

    /// <summary>Panier actif d'un acheteur (valorisé). Renvoie null si aucun panier ouvert.</summary>
    private static async Task<IResult> GetUserCartAsync(Guid id, ICartModuleApi cart, CancellationToken ct)
        => Results.Ok(await cart.GetActiveCartAsync(id, ct));

    /// <summary>Suspend un compte.</summary>
    private static async Task<IResult> SuspendUserAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new SuspendUserCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Réactive un compte.</summary>
    private static async Task<IResult> ReactivateUserAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ReactivateUserCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Attribue un rôle à un compte.</summary>
    private static async Task<IResult> AssignRoleAsync(Guid id, RoleRefRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new AssignRoleCommand(id, request.RoleId), ct)).Match(() => Results.NoContent());

    /// <summary>Retire un rôle d'un compte.</summary>
    private static async Task<IResult> RemoveRoleAsync(Guid id, Guid roleId, ISender sender, CancellationToken ct)
        => (await sender.Send(new RemoveRoleCommand(id, roleId), ct)).Match(() => Results.NoContent());

    /// <summary>Liste des rôles.</summary>
    private static async Task<IResult> ListRolesAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListRolesQuery(), ct)).Match(r => Results.Ok(r));

    /// <summary>Détail d'un rôle.</summary>
    private static async Task<IResult> GetRoleAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetRoleQuery(id), ct)).Match(r => Results.Ok(r));

    /// <summary>Crée un rôle.</summary>
    private static async Task<IResult> CreateRoleAsync(CreateRoleRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateRoleCommand(request.Name, request.Description, request.Permissions), ct)).Match(id => Results.Ok(new { roleId = id }));

    /// <summary>Met à jour un rôle.</summary>
    private static async Task<IResult> UpdateRoleAsync(Guid id, UpdateRoleRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UpdateRoleCommand(id, request.Name, request.Description), ct)).Match(() => Results.NoContent());

    /// <summary>Définit les permissions d'un rôle.</summary>
    private static async Task<IResult> SetPermissionsAsync(Guid id, PermissionsRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new SetRolePermissionsCommand(id, request.Permissions), ct)).Match(() => Results.NoContent());

    /// <summary>Supprime un rôle.</summary>
    private static async Task<IResult> DeleteRoleAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeleteRoleCommand(id), ct)).Match(() => Results.NoContent());

    public sealed record RegisterUserRequestBody(string FirstName, string LastName, string Email, string? PhoneNumber, string Password, IReadOnlyList<Guid>? RoleIds);
    public sealed record RoleRefRequest(Guid RoleId);
    public sealed record CreateRoleRequest(string Name, string? Description, IReadOnlyList<string>? Permissions);
    public sealed record UpdateRoleRequest(string Name, string? Description);
    public sealed record PermissionsRequest(IReadOnlyList<string> Permissions);
}
