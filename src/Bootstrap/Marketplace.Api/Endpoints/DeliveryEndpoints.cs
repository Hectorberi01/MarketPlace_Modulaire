using System.Security.Claims;
using HBA.Deliveries.Application.Deliveries.Commands;
using HBA.Deliveries.Application.Deliveries.Queries;
using HBA.Deliveries.Application.Drivers.Commands;
using HBA.Deliveries.Application.Drivers.Queries;
using HBA.Deliveries.Application.Pricing.Queries;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using HBA.Deliveries.Domain.Pricing;
using Marketplace.Api.Extensions;
using Marketplace.BuildingBlocks.Domain.Results;
using MediatR;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// API INTERNE DU MOTEUR LOGISTIQUE.
///
/// Destinée aux surfaces HBA : console d'administration et application livreur.
/// L'API PUBLIQUE des partenaires est une autre chose — versionnée, authentifiée
/// par clé, avec quotas — et vit sous /api/v1/deliveries.
///
/// TROIS GROUPES, TROIS NIVEAUX DE CONFIANCE
///
///   • /api/deliveries      consultation et création, utilisateur authentifié ;
///   • /api/deliveries/mine ce que fait LE livreur du jeton sur SES courses ;
///   • /api/drivers/me      ce que le livreur déclare sur lui-même.
///
/// ⚠️ CE QUI A CHANGÉ, ET POURQUOI CELA COMPTAIT.
///
/// Ces routes lisaient le « driverId » dans le CORPS DE REQUÊTE et le croyaient
/// sur parole. N'importe quel livreur authentifié pouvait donc accepter la course
/// d'un autre, la déclarer collectée, la déclarer livrée — et, depuis
/// l'introduction du partage de recette, en encaisser le gain. De même,
/// « /api/drivers/{id}/online » permettait de mettre un concurrent hors ligne.
///
/// Ce n'était pas théorique : un identifiant de course ou de livreur circule dans
/// les journaux, les captures d'écran et les tickets de support.
///
/// Le compte utilisateur, lui, vient du jeton signé et ne se falsifie pas. Toutes
/// les routes livreur le résolvent en identifiant de livreur, et les commandes
/// vérifient ensuite que la course lui est bien confiée.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DeliveryEndpoints
{
    public static IEndpointRouteBuilder MapDeliveryEndpoints(this IEndpointRouteBuilder app)
    {
        var deliveries = app.MapGroup("/api/deliveries").WithTags("Deliveries").RequireAuthorization();

        // « quote » n'est pas un GUID : la contrainte de route écarte l'ambiguïté.
        deliveries.MapPost("/quote", QuoteAsync).WithName("GetDeliveryQuote");

        deliveries.MapPost("/", CreateAsync).WithName("CreateDelivery");
        deliveries.MapGet("/{id:guid}", GetAsync).WithName("GetDelivery");
        deliveries.MapGet("/{id:guid}/tracking", GetTrackingAsync).WithName("GetDeliveryTracking");
        deliveries.MapPost("/{id:guid}/cancel", CancelAsync).WithName("CancelDelivery");

        // ── Ce que fait LE livreur du jeton, sur SES courses ─────────────────
        //
        // Groupe séparé, et préfixe « mine » explicite : le jour où quelqu'un
        // ajoutera une route ici, l'URL elle-même lui rappellera que l'appelant
        // est le livreur en charge, pas un administrateur.
        var mine = app.MapGroup("/api/deliveries/mine")
            .WithTags("Deliveries · Application livreur")
            .RequireAuthorization();

        mine.MapPost("/{id:guid}/accept", AcceptAsync).WithName("AcceptDelivery");
        mine.MapPost("/{id:guid}/reject", RejectAsync).WithName("RejectDelivery");
        mine.MapPost("/{id:guid}/arrived-at-pickup", ArrivedAtPickupAsync).WithName("DeliveryArrivedAtPickup");
        mine.MapPost("/{id:guid}/picked-up", PickedUpAsync).WithName("DeliveryPickedUp");
        mine.MapPost("/{id:guid}/in-transit", InTransitAsync).WithName("DeliveryInTransit");
        mine.MapPost("/{id:guid}/arrived-at-dropoff", ArrivedAtDropoffAsync).WithName("DeliveryArrivedAtDropoff");
        mine.MapPost("/{id:guid}/delivered", DeliveredAsync).WithName("DeliveryDelivered");

        // ── Ce que le livreur déclare sur lui-même ───────────────────────────
        //
        // « me » et non « {id} » : il n'y a plus d'identifiant à falsifier.
        var drivers = app.MapGroup("/api/drivers").WithTags("Deliveries · Livreurs").RequireAuthorization();

        drivers.MapPost("/", RegisterDriverAsync).WithName("RegisterDriver");
        drivers.MapPost("/me/online", GoOnlineAsync).WithName("DriverGoOnline");
        drivers.MapPost("/me/offline", GoOfflineAsync).WithName("DriverGoOffline");
        drivers.MapPost("/me/location", UpdateLocationAsync).WithName("UpdateDriverLocation");

        return app;
    }

    // ── Tarification ────────────────────────────────────────────────────────

    /// <summary>
    /// Le devis.
    ///
    /// ⚠️ APPELER CETTE ROUTE ÉCRIT EN BASE : un devis est persisté, valable
    /// quinze minutes, consommable une seule fois. Une interface qui recalculerait
    /// le prix à chaque frappe dans un champ remplirait la table.
    ///
    /// <c>PartnerId</c> reste NUL ici. Le renseigner depuis le JSON laisserait
    /// n'importe quel utilisateur authentifié émettre un devis au nom d'un
    /// partenaire, puis créer une course facturée à ce partenaire.
    /// </summary>
    private static async Task<IResult> QuoteAsync(QuoteRequestBody request, ISender sender, CancellationToken ct)
    {
        var query = new GetQuoteQuery(
            request.PickupLatitude, request.PickupLongitude,
            request.DropoffLatitude, request.DropoffLongitude,
            request.WeightKg, request.Size, request.Type, request.Vehicle,
            PartnerId: null);

        return (await sender.Send(query, ct)).Match(quote => Results.Ok(quote));
    }

    // ── Courses ─────────────────────────────────────────────────────────────

    private static async Task<IResult> CreateAsync(CreateDeliveryRequest request, ISender sender, CancellationToken ct)
    {
        // Une course de source externe se crée par l'API publique, où le
        // partenaire est établi par sa CLÉ D'API. L'accepter depuis le corps de
        // requête permettrait à tout utilisateur authentifié de faire facturer un
        // tiers. Sans ce refus explicite, l'échec porterait le message
        // « delivery.partner_required » — qui INVITE le prochain développeur à
        // ajouter le champ au JSON, c'est-à-dire à ouvrir la faille.
        if (request.Source is DeliverySource.ExternalPartner)
        {
            return Results.Problem(
                title: "Source réservée à l'API partenaire",
                detail: "Une course « ExternalPartner » se crée par l'API publique versionnée, où le "
                        + "partenaire est établi par la clé d'API. Cette route interne ne peut pas l'authentifier.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var command = new CreateDeliveryCommand(
            request.Reference,
            request.Source,
            request.Type,
            ToStop(request.Pickup),
            ToStop(request.Dropoff),
            new DeliveryPackageInput(
                request.Package.Description, request.Package.WeightKg,
                request.Package.IsFragile, request.Package.IsPerishable),
            request.RequiredProof,
            PartnerId: null,
            QuoteId: request.QuoteId,
            ScheduledForUtc: request.ScheduledForUtc);

        // 200 et non 201 : la création est IDEMPOTENTE par (référence, source).
        // Un rejeu renvoie la course existante, et annoncer « créé » serait faux.
        return (await sender.Send(command, ct)).Match(id => Results.Ok(new { deliveryId = id }));
    }

    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetDeliveryQuery(id), ct)).Match(delivery => Results.Ok(delivery));

    private static async Task<IResult> GetTrackingAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetDeliveryTrackingQuery(id), ct)).Match(tracking => Results.Ok(tracking));

    private static async Task<IResult> CancelAsync(
        Guid id, CancelDeliveryRequest? request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CancelDeliveryCommand(id, request?.Reason), ct)).Match(() => Results.NoContent());

    // ── Application livreur : le livreur vient du JETON ─────────────────────

    private static Task<IResult> AcceptAsync(Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => AsDriverAsync(user, sender, ct, driverId => sender.Send(new AcceptDeliveryCommand(id, driverId), ct));

    private static Task<IResult> RejectAsync(
        Guid id, RejectRequest? request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => AsDriverAsync(user, sender, ct,
            driverId => sender.Send(new RejectDeliveryCommand(id, driverId, request?.Reason), ct));

    private static Task<IResult> ArrivedAtPickupAsync(Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => AsDriverAsync(user, sender, ct, driverId => sender.Send(new MarkArrivedAtPickupCommand(id, driverId), ct));

    private static Task<IResult> PickedUpAsync(Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => AsDriverAsync(user, sender, ct, driverId => sender.Send(new MarkPickedUpCommand(id, driverId), ct));

    private static Task<IResult> InTransitAsync(Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => AsDriverAsync(user, sender, ct, driverId => sender.Send(new MarkInTransitCommand(id, driverId), ct));

    private static Task<IResult> ArrivedAtDropoffAsync(Guid id, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => AsDriverAsync(user, sender, ct, driverId => sender.Send(new MarkArrivedAtDropoffCommand(id, driverId), ct));

    private static Task<IResult> DeliveredAsync(
        Guid id, MarkDeliveredRequest? request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => AsDriverAsync(user, sender, ct,
            driverId => sender.Send(new MarkDeliveredCommand(id, request?.ProofValue, driverId), ct));

    // ── Livreurs ────────────────────────────────────────────────────────────

    /// <summary>
    /// Inscription livreur. Le <c>UserId</c> vient du JETON : laisser l'appelant le
    /// choisir permettrait d'inscrire n'importe qui comme livreur, y compris pour
    /// détourner ses gains.
    /// </summary>
    private static async Task<IResult> RegisterDriverAsync(
        RegisterDriverRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (UserId(user) is not { } userId)
        {
            return Unauthenticated();
        }

        return (await sender.Send(
                new RegisterDriverCommand(userId, request.FullName, request.Phone, request.Vehicle), ct))
            .Match(id => Results.Ok(new { driverId = id }));
    }

    private static Task<IResult> GoOnlineAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => AsDriverAsync(user, sender, ct, driverId => sender.Send(new GoOnlineCommand(driverId), ct));

    private static Task<IResult> GoOfflineAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => AsDriverAsync(user, sender, ct, driverId => sender.Send(new GoOfflineCommand(driverId), ct));

    /// <summary>
    /// Position du livreur. Route la plus APPELÉE de tout le module : une fois
    /// toutes les cinq à quinze secondes, par livreur en ligne. Elle n'écrit qu'en
    /// cache — voir UpdateDriverLocationCommandHandler pour la raison.
    ///
    /// ⚠️ Elle coûte désormais une résolution « compte → livreur » par appel. C'est
    /// le prix de ne plus faire confiance au corps de requête, et il est modeste :
    /// une lecture indexée. Si la charge le justifiait un jour, la bonne réponse
    /// serait de mettre le DriverId dans le JETON à la connexion — pas de revenir à
    /// un identifiant fourni par l'appelant.
    /// </summary>
    private static Task<IResult> UpdateLocationAsync(
        UpdateLocationRequest request, ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => AsDriverAsync(user, sender, ct,
            driverId => sender.Send(
                new UpdateDriverLocationCommand(driverId, request.Latitude, request.Longitude), ct));

    // ── Résolution du livreur ───────────────────────────────────────────────

    /// <summary>
    /// Résout le livreur depuis le jeton, puis exécute l'action. C'est le SEUL
    /// chemin par lequel un identifiant de livreur entre dans une commande depuis
    /// cette API — d'où le fait que toutes les routes livreur passent par ici
    /// plutôt que de répéter la résolution.
    /// </summary>
    private static async Task<IResult> AsDriverAsync(
        ClaimsPrincipal user, ISender sender, CancellationToken ct, Func<Guid, Task<Result>> action)
    {
        if (UserId(user) is not { } userId)
        {
            return Unauthenticated();
        }

        var driver = await sender.Send(new ResolveDriverQuery(userId), ct);
        if (driver.IsFailure)
        {
            return driver.Match(_ => Results.NoContent());
        }

        return (await action(driver.Value)).Match(() => Results.NoContent());
    }

    /// <summary>Identifiant du compte connecté (claim « sub » / NameIdentifier).</summary>
    private static Guid? UserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private static IResult Unauthenticated()
        => Results.Problem(
            title: "Compte non identifié",
            detail: "Le jeton ne porte pas d'identifiant de compte exploitable.",
            statusCode: StatusCodes.Status401Unauthorized);

    private static DeliveryStopInput ToStop(DeliveryStopRequest stop)
        => new(stop.ContactName, stop.Phone, stop.Commune, stop.Quartier,
               stop.Landmark, stop.Instructions, stop.Latitude, stop.Longitude);

    // ── Corps de requête ────────────────────────────────────────────────────

    /// <summary>
    /// Un point de la course. Repère, téléphone et position sont obligatoires côté
    /// domaine ; ils sont déclarés nullables ici pour qu'une omission produise un
    /// message métier clair plutôt qu'un 400 de désérialisation.
    /// </summary>
    public sealed record DeliveryStopRequest(
        string? ContactName,
        string? Phone,
        string? Commune,
        string? Quartier,
        string? Landmark,
        string? Instructions = null,
        double? Latitude = null,
        double? Longitude = null);

    public sealed record DeliveryPackageRequest(
        string? Description,
        decimal? WeightKg = null,
        bool IsFragile = false,
        bool IsPerishable = false);

    public sealed record CreateDeliveryRequest(
        string Reference,
        DeliverySource Source,
        DeliveryType Type,
        DeliveryStopRequest Pickup,
        DeliveryStopRequest Dropoff,
        DeliveryPackageRequest Package,
        ProofOfDeliveryKind RequiredProof = ProofOfDeliveryKind.None,

        // Devis obtenu par POST /api/deliveries/quote, de la forme « qt_… ».
        //
        // FACULTATIF, et c'est un choix qu'il faut connaître : une course créée
        // sans devis n'a AUCUN montant. Elle se déroule normalement, se livre
        // normalement, n'est facturable par personne — et ne produit aucun gain
        // pour le livreur, qui a pourtant roulé.
        string? QuoteId = null,

        // Heure de livraison souhaitée. Obligatoire pour le type « Scheduled »,
        // et REFUSÉE pour les autres : une date posée sur une course express
        // serait ignorée en silence, et personne ne comprendrait pourquoi le
        // créneau n'est pas respecté.
        DateTime? ScheduledForUtc = null);

    /// <summary>
    /// Demande de devis.
    ///
    /// ⚠️ <c>Vehicle</c> vaut mieux d'être OMIS. Il valait « moto » par défaut, et
    /// c'était le piège : un colis de 40 kg recevait un prix moto, puis aucune moto
    /// ne pouvait le porter et la course mourait faute de livreur. Laissé nul, le
    /// moteur retient le plus petit véhicule capable — donc le moins cher — et le
    /// renvoie dans la réponse.
    /// </summary>
    public sealed record QuoteRequestBody(
        double PickupLatitude,
        double PickupLongitude,
        double DropoffLatitude,
        double DropoffLongitude,
        decimal WeightKg,
        PackageSize Size = PackageSize.Small,
        DeliveryType Type = DeliveryType.Standard,
        VehicleType? Vehicle = null);

    public sealed record CancelDeliveryRequest(string? Reason);

    public sealed record MarkDeliveredRequest(string? ProofValue);

    public sealed record RejectRequest(string? Reason);

    /// <summary>
    /// Inscription livreur. Le compte utilisateur N'EST PAS dans ce corps : il
    /// vient du jeton.
    /// </summary>
    public sealed record RegisterDriverRequest(string FullName, string Phone, VehicleType Vehicle);

    public sealed record UpdateLocationRequest(double Latitude, double Longitude);
}
