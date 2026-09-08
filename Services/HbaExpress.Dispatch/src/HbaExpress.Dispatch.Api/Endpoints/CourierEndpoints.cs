using HbaExpress.Dispatch.Api.Extensions;
using HbaExpress.Dispatch.Api.Security;
using HbaExpress.Dispatch.Application.Couriers;
using HbaExpress.Dispatch.Application.Jobs;
using HbaExpress.Dispatch.Domain.Couriers;
using HbaExpress.Dispatch.Domain.Jobs;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HbaExpress.Dispatch.Api.Endpoints;

/// <summary>
/// L'APPLICATION COURSIER. Tout ce qu'un livreur fait de son téléphone.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// AUCUNE ROUTE NE PREND UN IDENTIFIANT DE COURSIER EN PARAMÈTRE.
///
/// Il est déduit du jeton, en deux temps : la claim donne le COMPTE, une lecture donne le
/// COURSIER (voir CourierPrincipal, qui explique pourquoi ce sont deux Guid distincts).
/// Une route « POST /couriers/{id}/online » aurait paru plus reposante et aurait permis à
/// n'importe quel porteur de jeton de mettre un autre coursier en ligne, de déplacer sa
/// position, ou d'accepter ses courses.
///
/// UN JETON VALIDE D'UN AUTRE PUBLIC — acheteur, vendeur — est cryptographiquement
/// accepté ici : les cinq hôtes partagent la clé de signature. Il ne correspond
/// simplement à aucun coursier, et reçoit 404. C'est le contrôle qui remplace, dans ce
/// service, le rôle que la marketplace n'a pas encore.
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

    private static Task<IResult> GoOnlineAsync(
        HttpContext http, CourierService couriers, CancellationToken ct)
        => WithCourierAsync(http, couriers, ct, id => couriers.GoOnlineAsync(id, ct));

    private static Task<IResult> GoOfflineAsync(
        HttpContext http, CourierService couriers, CancellationToken ct)
        => WithCourierAsync(http, couriers, ct, id => couriers.GoOfflineAsync(id, ct));

    /// <summary>
    /// Position du coursier — l'écriture la plus fréquente du service (toutes les quinze
    /// secondes par coursier en ligne).
    /// </summary>
    private static Task<IResult> UpdatePositionAsync(
        HttpContext http, PositionRequest request, CourierService couriers, CancellationToken ct)
        => WithCourierAsync(
            http, couriers, ct,
            id => couriers.UpdatePositionAsync(id, request.Latitude, request.Longitude, ct));

    private static Task<IResult> SetServiceAreaAsync(
        HttpContext http, ServiceAreaRequest request, CourierService couriers, CancellationToken ct)
        => WithCourierAsync(
            http, couriers, ct,
            id => couriers.SetServiceAreaAsync(id, request.CommuneCodes ?? new List<string>(), ct));

    private static async Task<IResult> ListOffersAsync(
        HttpContext http, CourierService couriers, IDeliveryJobRepository jobs,
        TimeProvider clock, CancellationToken ct)
    {
        var resolved = await ResolveAsync(http, couriers, ct);
        if (resolved.Failure is not null)
        {
            return resolved.Failure;
        }

        var courierId = resolved.CourierId!.Value;
        var offered = await jobs.ListOfferedToAsync(courierId, clock.GetUtcNow().UtcDateTime, ct);
        return Results.Ok(offered.Select(j => j.ToView(courierId)).ToList());
    }

    /// <summary>La course en cours, ou 204 s'il n'en a aucune.</summary>
    private static async Task<IResult> GetCurrentJobAsync(
        HttpContext http, CourierService couriers, IDeliveryJobRepository jobs, CancellationToken ct)
    {
        var resolved = await ResolveAsync(http, couriers, ct);
        if (resolved.Failure is not null)
        {
            return resolved.Failure;
        }

        var courierId = resolved.CourierId!.Value;
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
    private static Task<IResult> AcceptAsync(
        HttpContext http, Guid jobId, CourierService couriers, DeliveryJobService dispatch, CancellationToken ct)
        => WithCourierAsync(http, couriers, ct, id => dispatch.AcceptAsync(new DeliveryJobId(jobId), id, ct));

    private static Task<IResult> DeclineAsync(
        HttpContext http, Guid jobId, CourierService couriers, DeliveryJobService dispatch, CancellationToken ct)
        => WithCourierAsync(http, couriers, ct, id => dispatch.DeclineAsync(new DeliveryJobId(jobId), id, ct));

    private static Task<IResult> StartPickupAsync(
        HttpContext http, Guid jobId, CourierService couriers, JobProgressService progress, CancellationToken ct)
        => WithCourierAsync(http, couriers, ct, id => progress.StartPickupAsync(new DeliveryJobId(jobId), id, ct));

    private static Task<IResult> MarkPickedUpAsync(
        HttpContext http, Guid jobId, Guid pickupId, CourierService couriers,
        JobProgressService progress, CancellationToken ct)
        => WithCourierAsync(
            http, couriers, ct,
            id => progress.MarkPickedUpAsync(new DeliveryJobId(jobId), id, pickupId, ct));

    private static Task<IResult> ReportIncidentAsync(
        HttpContext http, Guid jobId, Guid pickupId, ReasonRequest request,
        CourierService couriers, JobProgressService progress, CancellationToken ct)
        => WithCourierAsync(
            http, couriers, ct,
            id => progress.ReportPickupIncidentAsync(
                new DeliveryJobId(jobId), id, pickupId, request.Reason ?? string.Empty, ct));

    private static Task<IResult> StartTransitAsync(
        HttpContext http, Guid jobId, CourierService couriers, JobProgressService progress, CancellationToken ct)
        => WithCourierAsync(http, couriers, ct, id => progress.StartTransitAsync(new DeliveryJobId(jobId), id, ct));

    private static Task<IResult> MarkDeliveredAsync(
        HttpContext http, Guid jobId, CourierService couriers, JobProgressService progress, CancellationToken ct)
        => WithCourierAsync(http, couriers, ct, id => progress.MarkDeliveredAsync(new DeliveryJobId(jobId), id, ct));

    private static Task<IResult> FailAsync(
        HttpContext http, Guid jobId, ReasonRequest request,
        CourierService couriers, JobProgressService progress, CancellationToken ct)
        => WithCourierAsync(
            http, couriers, ct,
            id => progress.FailAsync(new DeliveryJobId(jobId), id, request.Reason ?? string.Empty, ct));

    /// <summary>Résout le coursier appelant, puis exécute.</summary>
    private static async Task<IResult> WithCourierAsync(
        HttpContext http, CourierService couriers, CancellationToken cancellationToken,
        Func<CourierId, Task<Result>> action)
    {
        var resolved = await ResolveAsync(http, couriers, cancellationToken);
        if (resolved.Failure is not null)
        {
            return resolved.Failure;
        }

        var result = await action(resolved.CourierId!.Value);
        return result.Match(Results.NoContent);
    }

    /// <summary>
    /// Du jeton au coursier, avec deux refus distincts et délibérément différents.
    ///
    ///  • 401 — le jeton ne porte aucun identifiant de compte exploitable. Il est
    ///    malformé ; l'application doit se reconnecter.
    ///  • 404 — le compte est valide mais n'est rattaché à aucun coursier connu de ce
    ///    service. C'est le cas d'un acheteur ou d'un vendeur (leurs jetons sont
    ///    cryptographiquement valables ici, la clé étant partagée), et celui d'un
    ///    coursier que la marketplace n'a pas encore annoncé. Se reconnecter n'y
    ///    changerait rien, et le dire évite à l'application de boucler.
    /// </summary>
    private static async Task<(IResult? Failure, CourierId? CourierId)> ResolveAsync(
        HttpContext http, CourierService couriers, CancellationToken cancellationToken)
    {
        if (CourierPrincipal.ResolveUserId(http.User) is not { } userId)
        {
            return (Results.Unauthorized(), null);
        }

        var courierId = await couriers.ResolveByUserAsync(userId, cancellationToken);
        if (courierId is null)
        {
            return (
                Results.Problem(
                    title: "courier.not_found",
                    detail: "Ce compte n'est rattaché à aucun coursier connu du service de dispatch.",
                    statusCode: StatusCodes.Status404NotFound),
                null);
        }

        return (null, courierId);
    }

    public sealed record PositionRequest(double Latitude, double Longitude);

    public sealed record ServiceAreaRequest(List<string>? CommuneCodes);

    public sealed record ReasonRequest(string? Reason);
}
