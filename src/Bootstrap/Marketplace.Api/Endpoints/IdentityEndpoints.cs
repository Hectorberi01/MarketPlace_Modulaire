using System.Security.Claims;
using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Identity.Application.Roles.Commands.CreateRole;
using Marketplace.Modules.Identity.Application.Roles.Commands.DeleteRole;
using Marketplace.Modules.Identity.Application.Roles.Commands.SetRolePermissions;
using Marketplace.Modules.Identity.Application.Roles.Commands.UpdateRole;
using Marketplace.Modules.Identity.Application.Roles.Queries.GetRole;
using Marketplace.Modules.Identity.Application.Roles.Queries.ListRoles;
using Marketplace.Modules.Identity.Application.Users.Commands.AssignRole;
using Marketplace.Modules.Identity.Application.Users.Commands.ChangePassword;
using Marketplace.Modules.Identity.Application.Users.Commands.ConfirmEmail;
using Marketplace.Modules.Identity.Application.Users.Commands.Login;
using Marketplace.Modules.Identity.Application.Users.Commands.Logout;
using Marketplace.Modules.Identity.Application.Users.Commands.Mfa;
using Marketplace.Modules.Identity.Application.Users.Commands.ReactivateUser;
using Marketplace.Modules.Identity.Application.Users.Commands.RefreshToken;
using Marketplace.Modules.Identity.Application.Users.Commands.RegisterUser;
using Marketplace.Modules.Identity.Application.Users.Commands.RemoveRole;
using Marketplace.Modules.Identity.Application.Users.Commands.SuspendUser;
using Marketplace.Modules.Identity.Application.Users.Commands.UpdateProfile;
using Marketplace.Modules.Identity.Application.Users.Queries.GetUser;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Identity : authentification (anonyme), gestion de son
/// propre compte (authentifié) et administration des comptes et rôles (Admin).
/// </summary>
public static class IdentityEndpoints
{
    /// <summary>Enregistre les routes auth, compte et administration.</summary>
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        MapAuth(app);
        MapAccount(app);
        MapAdminUsers(app);
        MapRoles(app);
        return app;
    }

    // -------------------------------------------------------------------- Auth

    private static void MapAuth(IEndpointRouteBuilder app)
    {
        // Politique « auth » (30 req/min/IP) comme sur les quatre BFF. C'était le seul
        // groupe d'authentification à retomber sur la limite globale de 300/min —
        // dix fois plus permissif sur les routes qui reçoivent des mots de passe.
        // Cet hôte n'est plus routé à l'edge, ce qui atténue sans corriger.
        var group = app.MapGroup("/api/identity/auth").WithTags("Identity · Auth")
            .RequireRateLimiting(AuthRateLimiter.PolicyName);

        group.MapPost("/register", RegisterAsync).WithName("Register").AllowAnonymous();
        group.MapPost("/confirm-email", ConfirmEmailAsync).WithName("ConfirmEmail").AllowAnonymous();
        group.MapPost("/login", LoginAsync).WithName("Login").AllowAnonymous();
        group.MapPost("/refresh", RefreshAsync).WithName("RefreshToken").AllowAnonymous();
    }

    /// <summary>Inscrit un nouvel utilisateur ; un e-mail de vérification est envoyé.</summary>
    private static async Task<IResult> RegisterAsync(RegisterRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(
            new RegisterUserCommand(request.FirstName, request.LastName, request.Email, request.PhoneNumber, request.Password), ct);
        return result.Match(id => Results.Created($"/api/identity/users/{id}", new { id }));
    }

    /// <summary>Confirme l'adresse e-mail à partir du jeton reçu par lien.</summary>
    private static async Task<IResult> ConfirmEmailAsync(ConfirmEmailRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ConfirmEmailCommand(request.UserId, request.Token), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Authentifie l'utilisateur ; renvoie les jetons ou l'exigence d'un code MFA.</summary>
    private static async Task<IResult> LoginAsync(LoginRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new LoginCommand(request.Email, request.Password, request.MfaCode), ct);
        return result.Match(response => Results.Ok(response));
    }

    /// <summary>Échange un refresh token valide contre une nouvelle paire de jetons.</summary>
    private static async Task<IResult> RefreshAsync(RefreshRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new RefreshTokenCommand(request.RefreshToken), ct);
        return result.Match(tokens => Results.Ok(tokens));
    }

    // ----------------------------------------------------------------- Account

    private static void MapAccount(IEndpointRouteBuilder app)
    {
        var group = app.MapAuthenticatedGroup("/api/identity/account").WithTags("Identity · Account");

        group.MapGet("/me", GetMeAsync).WithName("GetMe");
        group.MapPut("/me", UpdateMeAsync).WithName("UpdateProfile");
        group.MapPost("/me/change-password", ChangePasswordAsync).WithName("ChangePassword");
        group.MapPost("/me/logout", LogoutAsync).WithName("Logout");
        group.MapPost("/me/mfa/setup", BeginMfaAsync).WithName("BeginMfaSetup");
        group.MapPost("/me/mfa/confirm", ConfirmMfaAsync).WithName("ConfirmMfa");
        group.MapPost("/me/mfa/disable", DisableMfaAsync).WithName("DisableMfa");
    }

    /// <summary>Renvoie le profil de l'utilisateur authentifié.</summary>
    private static async Task<IResult> GetMeAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new GetUserQuery(userId), ct);
        return result.Match(summary => Results.Ok(summary));
    }

    /// <summary>Met à jour le profil de l'utilisateur authentifié.</summary>
    private static async Task<IResult> UpdateMeAsync(UpdateProfileRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new UpdateUserProfileCommand(userId, request.FirstName, request.LastName, request.PhoneNumber), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Change le mot de passe de l'utilisateur authentifié.</summary>
    private static async Task<IResult> ChangePasswordAsync(ChangePasswordRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ChangePasswordCommand(userId, request.CurrentPassword, request.NewPassword), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Déconnecte un appareil en révoquant son refresh token.</summary>
    private static async Task<IResult> LogoutAsync(LogoutRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new LogoutCommand(userId, request.RefreshToken), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Initie l'activation MFA : renvoie le secret et l'URI otpauth (QR code).</summary>
    private static async Task<IResult> BeginMfaAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new BeginMfaSetupCommand(userId), ct);
        return result.Match(setup => Results.Ok(setup));
    }

    /// <summary>Confirme l'activation MFA avec un premier code TOTP.</summary>
    private static async Task<IResult> ConfirmMfaAsync(MfaCodeRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ConfirmMfaCommand(userId, request.Code), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Désactive la MFA après vérification d'un code TOTP.</summary>
    private static async Task<IResult> DisableMfaAsync(MfaCodeRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new DisableMfaCommand(userId, request.Code), ct);
        return result.Match(() => Results.NoContent());
    }

    // ------------------------------------------------------------- Admin users

    private static void MapAdminUsers(IEndpointRouteBuilder app)
    {
        var group = app.MapAdminGroup("/api/identity/users")
            .WithTags("Identity · Admin Users")
            .RequireAuthorization(policy => policy.RequireRole("Admin"));

        group.MapGet("/{id:guid}", GetUserAsync).WithName("GetUser");
        group.MapPost("/{id:guid}/suspend", SuspendUserAsync).WithName("SuspendUser");
        group.MapPost("/{id:guid}/reactivate", ReactivateUserAsync).WithName("ReactivateUser");
        group.MapPost("/{id:guid}/roles", AssignRoleAsync).WithName("AssignRole");
        group.MapDelete("/{id:guid}/roles/{roleId:guid}", RemoveRoleAsync).WithName("RemoveRole");
    }

    /// <summary>Récupère un compte par son identifiant (Admin).</summary>
    private static async Task<IResult> GetUserAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetUserQuery(id), ct);
        return result.Match(summary => Results.Ok(summary));
    }

    /// <summary>Suspend un compte (Admin).</summary>
    private static async Task<IResult> SuspendUserAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new SuspendUserCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Réactive un compte suspendu (Admin).</summary>
    private static async Task<IResult> ReactivateUserAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ReactivateUserCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Assigne un rôle à un compte (Admin).</summary>
    private static async Task<IResult> AssignRoleAsync(Guid id, AssignRoleRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new AssignRoleCommand(id, request.RoleId), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Retire un rôle d'un compte (Admin).</summary>
    private static async Task<IResult> RemoveRoleAsync(Guid id, Guid roleId, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new RemoveRoleCommand(id, roleId), ct);
        return result.Match(() => Results.NoContent());
    }

    // ------------------------------------------------------------------ Roles

    private static void MapRoles(IEndpointRouteBuilder app)
    {
        var group = app.MapAdminGroup("/api/identity/roles")
            .WithTags("Identity · Roles")
            .RequireAuthorization(policy => policy.RequireRole("Admin"));

        group.MapGet("/", ListRolesAsync).WithName("ListRoles");
        group.MapGet("/{id:guid}", GetRoleAsync).WithName("GetRole");
        group.MapPost("/", CreateRoleAsync).WithName("CreateRole");
        group.MapPut("/{id:guid}", UpdateRoleAsync).WithName("UpdateRole");
        group.MapPut("/{id:guid}/permissions", SetRolePermissionsAsync).WithName("SetRolePermissions");
        group.MapDelete("/{id:guid}", DeleteRoleAsync).WithName("DeleteRole");
    }

    /// <summary>Liste tous les rôles (Admin).</summary>
    private static async Task<IResult> ListRolesAsync(ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListRolesQuery(), ct);
        return result.Match(roles => Results.Ok(roles));
    }

    /// <summary>Récupère un rôle et ses permissions (Admin).</summary>
    private static async Task<IResult> GetRoleAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetRoleQuery(id), ct);
        return result.Match(role => Results.Ok(role));
    }

    /// <summary>Crée un rôle (Admin).</summary>
    private static async Task<IResult> CreateRoleAsync(CreateRoleRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new CreateRoleCommand(request.Name, request.Description, request.Permissions), ct);
        return result.Match(id => Results.Created($"/api/identity/roles/{id}", new { id }));
    }

    /// <summary>Met à jour le nom et la description d'un rôle (Admin).</summary>
    private static async Task<IResult> UpdateRoleAsync(Guid id, UpdateRoleRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new UpdateRoleCommand(id, request.Name, request.Description), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Remplace l'ensemble des permissions d'un rôle (Admin).</summary>
    private static async Task<IResult> SetRolePermissionsAsync(Guid id, SetPermissionsRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new SetRolePermissionsCommand(id, request.Permissions), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Supprime un rôle non-système (Admin).</summary>
    private static async Task<IResult> DeleteRoleAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new DeleteRoleCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    // -------------------------------------------------------------- Utilitaire

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    // ------------------------------------------------------- Contrats d'entrée

    public sealed record RegisterRequest(string FirstName, string LastName, string Email, string PhoneNumber, string Password);
    public sealed record ConfirmEmailRequest(Guid UserId, string Token);
    public sealed record LoginRequest(string Email, string Password, string? MfaCode);
    public sealed record RefreshRequest(string RefreshToken);
    public sealed record LogoutRequest(string RefreshToken);
    public sealed record UpdateProfileRequest(string FirstName, string LastName, string PhoneNumber);
    public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public sealed record MfaCodeRequest(string Code);
    public sealed record AssignRoleRequest(Guid RoleId);
    public sealed record CreateRoleRequest(string Name, string? Description, IReadOnlyList<string>? Permissions);
    public sealed record UpdateRoleRequest(string Name, string? Description);
    public sealed record SetPermissionsRequest(IReadOnlyList<string> Permissions);
}
