using HbaExpress.Dispatch.Api.Extensions;
using HbaExpress.Dispatch.Api.Security;
using HbaExpress.Dispatch.Application.Couriers;
using HbaExpress.Dispatch.Application.Jobs;
using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;

namespace HbaExpress.Dispatch.Api.Endpoints;

/// <summary>
/// L'APPLICATION COURSIER. Tout ce qu'un livreur fait de son téléphone.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// AUCUNE ROUTE NE PREND UN IDENTIFIANT DE COURSIER EN PARAMÈTRE.
///
/// Il vient du jeton, et de lui seul (voir CourierPrincipal). Une route
/// « POST /couriers/{id}/online » aurait paru plus reposante et aurait permis à
/// n'importe quel porteur de jeton de mettre un autre coursier en ligne, de déplacer sa
/// position, ou d'accepter ses courses.
///
/// LA LECTURE DES OFFRES EXISTE PARCE QUE LE TEMPS RÉEL EST BEST-EFFORT. Le hub poussera
/// les offres quand il existera ; il pourra toujours les perdre — veille du téléphone,
/// réseau coupé, application relancée. GET /offers est le rattrapage, et c'est lui qui
/// rend le service utilisable AVANT que le hub ne soit écrit.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class CourierEndpoints
{
    public static IEndpointRouteBuilder MapCourierEndpoints(this IEndpointRouteBuilder app)
    {
        // RequireAuthorization explicite, bien que la politique de repli l'impose déjà :
        // la redondance coûte une ligne, et l'oubli coûterait une flotte ouverte.
        var courier = app.MapGroup("/api/courier").WithTags("Courier").RequireAuthorization();

        courier.MapPost("/online", GoOnlineAsync).WithName("CourierGoOnline");
        courier.MapPost("/offline", GoOfflineAsync).WithName("CourierGoOffline");
        courier.MapPut("/position", UpdatePositionAsync).WithName("CourierUpdatePosition");
        courier.MapPut("/service-area", SetServiceAreaAsync).WithName("CourierSetServiceArea");

        courier.MapGet("/offers", ListOffersAsync).WithName("CourierListOffers");
        courier.MapGet("/jobs/current", GetCurrentJobAsync).WithName("CourierCurrentJob");

        courier.MapPost("/jobs/{jobId:guid}/accept", AcceptAsync).WithName("CourierAcceptJob");
        courier.MapPost("/jobs/{jobId:guid}/decline", DeclineAsync).WithName("CourierDeclineJob");

        courier.MapPost("/jobs/{jobId:guid}/pickup", StartPickupAsync).WithName("CourierStartPickup");
        courier.MapPost("/jobs/{jobId:guid}/pickups/{pickupId:guid}/collected", MarkPickedUpAsync)
            .WithName("CourierMarkPickedUp");
        courier.MapPost("/jobs/{jobId:guid}/pickups/{pickupId:guid}/incident", ReportIncidentAsync)
            .WithName("CourierReportPickupIncident");
        courier.MapPost("/jobs/{jobId:guid}/transit", StartTransitAsync).WithName("CourierStartTransit");
        courier.MapPost("/jobs/{jobId:guid}/delivered", MarkDeliveredAsync).WithName("CourierMarkDelivered");
        courier.MapPost("/jobs/{jobId:guid}/failed", FailAsync).WithName("CourierFailJob");

        return app;
    }

    private static async Task<IResult> GoOnlineAsync(HttpContext http, CourierService couriers, CancellationToken ct)
        => await WithCourierAsync(http, id => couriers.GoOnlineAsync(id, ct));

    private static async Task<IResult> GoOfflineAsync(HttpContext http, CourierService couriers, CancellationToken ct)
        => await WithCourierAsync(http, id => couriers.GoOfflineAsync(id, ct));

    /// <summary>
    /// Position du coursier — l'écriture la plus fréquente du service (toutes les quinze
    /// secondes par coursier en ligne).
    /// </summary>
    private static async Task<IResult> UpdatePositionAsync(
        HttpContext http, PositionRequest request, CourierService couriers, CancellationToken ct)
        => await WithCourierAsync(
            http, id => couriers.UpdatePositionAsync(id, request.Latitude, request.Longitude, ct));

    private static async Task<IResult> SetServiceAreaAsync(
        HttpContext http, ServiceAreaRequest request, CourierService couriers, CancellationToken ct)
        => await WithCourierAsync(
            http, id => couriers.SetServiceAreaAsync(id, request.CommuneCodes ?? new List<string>(), ct));

    private static async Task<IResult> ListOffersAsync(
        HttpContext http, IDeliveryJobRepository jobs, TimeProvider clock, CancellationToken ct)
    {
        if (CourierPrincipal.Resolve(http.User) is not { } courierId)
        {
            return Results.Unauthorized();
        }

        var offered = await jobs.ListOfferedToAsync(courierId, clock.GetUtcNow().UtcDateTime, ct);
        return Results.Ok(offered.Select(j => j.ToView(courierId)).ToList());
    }

    /// <summary>La course en cours, ou 204 s'il n'en a aucune.</summary>
    private static async Task<IResult> GetCurrentJobAsync(
        HttpContext http, IDeliveryJobRepository jobs, CancellationToken ct)
    {
        if (CourierPrincipal.Resolve(http.User) is not { } courierId)
        {
            return Results.Unauthorized();
        }

        var job = await jobs.GetActiveForCourierAsync(courierId, ct);
        return job is null ? Results.NoContent() : Results.Ok(job.ToView(courierId));
    }

    /// <summary>
    /// Prendre la course.
    ///
    /// LE PERDANT D'UNE COURSE SIMULTANÉE REÇOIT UN 409, PAS UN 500. Deux chemins y
    /// mènent — la règle métier (job.already_taken) quand le gagnant est déjà enregistré,
    /// et le jeton de concurrence quand les deux acceptations se croisent avant
    /// d'atteindre la base. Les deux produisent le même code, ce qui laisse une seule
    /// branche à écrire côté application. Voir ConcurrencyExceptionHandler.
    /// </summary>
    private static async Task<IResult> AcceptAsync(
        HttpContext http, Guid jobId, DeliveryJobService dispatch, CancellationToken ct)
        => await WithCourierAsync(http, id => dispatch.AcceptAsync(new DeliveryJobId(jobId), id, ct));

    private static async Task<IResult> DeclineAsync(
        HttpContext http, Guid jobId, DeliveryJobService dispatch, CancellationToken ct)
        => await WithCourierAsync(http, id => dispatch.DeclineAsync(new DeliveryJobId(jobId), id, ct));

    private static async Task<IResult> StartPickupAsync(
        HttpContext http, Guid jobId, JobProgressService progress, CancellationToken ct)
        => await WithCourierAsync(http, id => progress.StartPickupAsync(new DeliveryJobId(jobId), id, ct));

    private static async Task<IResult> MarkPickedUpAsync(
        HttpContext http, Guid jobId, Guid pickupId, JobProgressService progress, CancellationToken ct)
        => await WithCourierAsync(
            http, id => progress.MarkPickedUpAsync(new DeliveryJobId(jobId), id, pickupId, ct));

    private static async Task<IResult> ReportIncidentAsync(
        HttpContext http, Guid jobId, Guid pickupId, ReasonRequest request,
        JobProgressService progress, CancellationToken ct)
        => await WithCourierAsync(
            http,
            id => progress.ReportPickupIncidentAsync(
                new DeliveryJobId(jobId), id, pickupId, request.Reason ?? string.Empty, ct));

    private static async Task<IResult> StartTransitAsync(
        HttpContext http, Guid jobId, JobProgressService progress, CancellationToken ct)
        => await WithCourierAsync(http, id => progress.StartTransitAsync(new DeliveryJobId(jobId), id, ct));

    private static async Task<IResult> MarkDeliveredAsync(
        HttpContext http, Guid jobId, JobProgressService progress, CancellationToken ct)
        => await WithCourierAsync(http, id => progress.MarkDeliveredAsync(new DeliveryJobId(jobId), id, ct));

    private static async Task<IResult> FailAsync(
        HttpContext http, Guid jobId, ReasonRequest request, JobProgressService progress, CancellationToken ct)
        => await WithCourierAsync(
            http, id => progress.FailAsync(new DeliveryJobId(jobId), id, request.Reason ?? string.Empty, ct));

    /// <summary>
    /// Résout le coursier appelant, puis exécute. Un jeton valide SANS identifiant
    /// exploitable donne un 401 : c'est un jeton d'un autre public (un acheteur, un
    /// vendeur), pas un coursier.
    /// </summary>
    private static async Task<IResult> WithCourierAsync(
        HttpContext http, Func<CourierId, Task<Marketplace.BuildingBlocks.Domain.Results.Result>> action)
    {
        if (CourierPrincipal.Resolve(http.User) is not { } courierId)
        {
            return Results.Unauthorized();
        }

        var result = await action(courierId);
        return result.Match(Results.NoContent);
    }

    public sealed record PositionRequest(double Latitude, double Longitude);

    public sealed record ServiceAreaRequest(List<string>? CommuneCodes);

    public sealed record ReasonRequest(string? Reason);
}
