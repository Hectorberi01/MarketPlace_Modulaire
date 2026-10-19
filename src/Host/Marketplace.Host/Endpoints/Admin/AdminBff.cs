using System.Security.Claims;

namespace Marketplace.Host.Admin.Endpoints;

/// <summary>
/// Utilitaires partagés des endpoints /admin. Toutes les routes exigent le rôle
/// Admin ; la modération (avis, litiges) accepte aussi Moderator. Les helpers de
/// groupe centralisent ces politiques.
/// </summary>
internal static class AdminBff
{
    public const string AdminRole = "Admin";
    public const string ModeratorRole = "Moderator";

    /// <summary>Groupe protégé : rôle Admin requis.</summary>
    public static RouteGroupBuilder MapAdminGroup(this IEndpointRouteBuilder app, string prefix, string tag)
        => app.MapGroup(prefix).WithTags(tag).RequireAuthorization(p => p.RequireRole(AdminRole));

    /// <summary>Groupe protégé : rôle Admin ou Moderator requis (modération).</summary>
    public static RouteGroupBuilder MapModerationGroup(this IEndpointRouteBuilder app, string prefix, string tag)
        => app.MapGroup(prefix).WithTags(tag).RequireAuthorization(p => p.RequireRole(AdminRole, ModeratorRole));

    /// <summary>Identifiant de l'administrateur courant, extrait du JWT.</summary>
    public static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>Réponse 501 normalisée pour une capacité non encore exposée par un module.</summary>
    public static IResult NotImplemented(string capability)
        => Results.Json(new { error = "not_implemented", capability }, statusCode: StatusCodes.Status501NotImplemented);
}
