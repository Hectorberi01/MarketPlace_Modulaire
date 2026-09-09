using System.Security.Claims;
using Marketplace.Modules.Delivery.Contracts;

namespace Marketplace.Bff.Driver.Endpoints;

/// <summary>
/// Le compte coursier — /driver/me.
///
/// C'est la route que l'application appelle en premier, et c'est elle qui répond à la
/// question « ce compte est-il un coursier ? ». Un 404 « not_a_courier » y est une
/// réponse NORMALE, pas une erreur : c'est ce que voit un acheteur qui installerait
/// l'application coursier, et l'écran d'accueil doit savoir le dire.
/// </summary>
public static class DriverAccountEndpoints
{
    public static IEndpointRouteBuilder MapDriverAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapDriverGroup("/driver", "Driveur · Compte");

        me.MapGet("/me", MeAsync).WithName("DriverMe");

        return app;
    }

    /// <summary>Statut du coursier et coordonnées de versement.</summary>
    private static async Task<IResult> MeAsync(
        ClaimsPrincipal user, IDeliveryModuleApi couriers, CancellationToken ct)
    {
        if (DriverBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var courier = await couriers.GetCourierByUserAsync(userId, ct);
        return courier is null ? Results.NotFound(new { error = "not_a_courier" }) : Results.Ok(courier);
    }
}
