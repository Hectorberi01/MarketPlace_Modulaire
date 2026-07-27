using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Cart.Contracts;
using Marketplace.Modules.Identity.Application.Roles.Commands.CreateRole;
using Marketplace.Modules.Identity.Application.Roles.Commands.DeleteRole;
using Marketplace.Modules.Identity.Application.Roles.Commands.SetRolePermissions;
using Marketplace.Modules.Identity.Application.Roles.Commands.UpdateRole;
using Marketplace.Modules.Identity.Application.Roles.Queries.GetRole;
using Marketplace.Modules.Identity.Application.Roles.Queries.ListRoles;
using Marketplace.Modules.Identity.Application.Users.Commands.ApproveUser;
using Marketplace.Modules.Identity.Application.Users.Commands.AssignRole;
using Marketplace.Modules.Identity.Application.Users.Commands.MarkEmailVerified;
using Marketplace.Modules.Identity.Application.Users.Commands.ReactivateUser;
using Marketplace.Modules.Identity.Application.Users.Commands.RemoveRole;
using Marketplace.Modules.Identity.Application.Users.Commands.SuspendUser;
using Marketplace.Modules.Identity.Application.Users.Queries.GetUser;
using Marketplace.Modules.Identity.Application.Users.Queries.ListUsers;
using Marketplace.Modules.Identity.Application.Users.Queries.GetSignupsSeries;
using Marketplace.Modules.Identity.Application.Users.Commands.RegisterUser;
using Marketplace.Modules.Ordering.Application.Orders.Queries;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>BFF Admin — comptes & rôles : suspension/réactivation, gestion des rôles et permissions (RBAC).</summary>
public static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapAdminGroup("/admin/users", "Admin · Comptes");
        users.MapGet("/", ListUsersAsync);
        users.MapGet("/signups", SignupsAsync);
        users.MapPost("/register", RegisterUserAsync);
        users.MapGet("/{id:guid}", GetUserAsync);
        users.MapGet("/{id:guid}/cart", GetUserCartAsync);
        // Commandes du client (sert au remboursement direct : choisir la commande).
        users.MapGet("/{id:guid}/orders", GetUserOrdersAsync);
        // Validation d'un compte en attente. « Refuser » n'a pas d'endpoint dédié :
        // c'est /suspend. Un refus doit rester réversible et laisser une trace —
        // supprimer un compte douteux effacerait précisément ce qu'on veut garder.
        users.MapPost("/{id:guid}/approve", ApproveUserAsync);
        users.MapPost("/{id:guid}/suspend", SuspendUserAsync);
        users.MapPost("/{id:guid}/reactivate", ReactivateUserAsync);

        // Attestation d'e-mail par un administrateur. Route distincte d'/approve, parce
        // que les deux gestes le sont : vérifier une adresse ne vaut pas autoriser
        // l'accès, et autoriser l'accès ne prouve rien sur l'adresse.
        users.MapPost("/{id:guid}/verify-email", MarkEmailVerifiedAsync);
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

    /// <summary>Page de comptes (pagination, recherche prénom/nom, filtre statut).</summary>
    private static async Task<IResult> ListUsersAsync(
        ISender sender, CancellationToken ct,
        int page = 1, int pageSize = 20, string? search = null, string? status = null, string? sort = null, string? dir = null)
        => (await sender.Send(new ListUsersQuery(page, pageSize, search, status, sort, dir), ct)).Match(u => Results.Ok(u));

    /// <summary>Évolution des inscriptions par jour sur les N derniers jours (défaut 30).</summary>
    private static async Task<IResult> SignupsAsync(ISender sender, CancellationToken ct, int days = 30)
    {
        var toUtc = DateTime.UtcNow.Date.AddDays(1);          // borne haute exclusive : inclut aujourd'hui
        var fromUtc = toUtc.AddDays(-Math.Clamp(days, 1, 365));
        return (await sender.Send(new GetSignupsSeriesQuery(fromUtc, toUtc), ct)).Match(s => Results.Ok(s));
    }

    /// <summary>Crée un compte (par ex. un administrateur) et lui attribue les rôles fournis.</summary>
    private static async Task<IResult> RegisterUserAsync(RegisterUserRequestBody request, ISender sender, CancellationToken ct)
    {
        // `CreatedByAdmin: true` — le compte naît actif (sauf si la configuration
        // exige explicitement une seconde relecture). C'est vous qui venez de le
        // créer : vous auto-valider n'apporterait rien.
        var userResult = await sender.Send(new RegisterUserCommand(
            request.FirstName, request.LastName, request.Email, request.PhoneNumber ?? string.Empty,
            request.Password, CreatedByAdmin: true), ct);
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

    /// <summary>Commandes passées par ce client (les plus récentes d'abord).</summary>
    private static async Task<IResult> GetUserOrdersAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListMyOrdersQuery(id), ct)).Match(o => Results.Ok(o));

    /// <summary>Suspend un compte.</summary>
    /// <summary>Valide un compte en attente : il devient actif et peut se connecter.</summary>
    private static async Task<IResult> ApproveUserAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ApproveUserCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>
    /// Marque l'e-mail comme vérifié, sur attestation de l'administrateur.
    /// Tracé dans <c>EmailVerifiedByAdminOnUtc</c> : ce n'est pas une preuve, c'est
    /// une confiance, et la base doit pouvoir le dire.
    /// </summary>
    private static async Task<IResult> MarkEmailVerifiedAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new MarkEmailVerifiedCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Suspend un compte — c'est aussi la forme que prend un refus.</summary>
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
