using System.Text.Json;
using HbaExpress.Dispatch.Api.Extensions;
using HbaExpress.Dispatch.Api.Security;
using HbaExpress.Dispatch.Application.Couriers;
using HbaExpress.Dispatch.Application.Jobs;
using HbaExpress.Dispatch.Domain.Couriers;

namespace HbaExpress.Dispatch.Api.Endpoints;

/// <summary>
/// CE QUE LA MARKETPLACE DIT AU SERVICE DE DISPATCH.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// TROIS ANNONCES, ET LE SERVICE NE SAIT RIEN SANS ELLES.
///
///  • COMMANDE PRÊTE — la seule source de courses. Sans elle, le dispatch tourne à vide.
///  • COMMANDE ANNULÉE — sinon un coursier roule vers une commande qui n'existe plus, et
///    reste marqué « en course » pour toujours.
///  • STATUT DU COURSIER — c'est aussi par là qu'un coursier ENTRE dans ce service : il
///    n'y a pas d'inscription séparée, et il ne peut pas s'y déclarer actif lui-même.
///
/// AUCUNE N'EST AUTHENTIFIÉE PAR JETON, ET C'EST NORMAL : l'appelant est un SERVICE, pas
/// une personne. Son authentification est la SIGNATURE du corps brut — vérifiée sur
/// chaque route, fermée par défaut. Le groupe est donc anonyme au sens ASP.NET, et
/// nullement ouvert.
///
/// LES CHARGES SONT DES TYPES À NOUS. Elles ont la forme des événements d'intégration du
/// monolithe, parce que c'est ce qu'il émet — mais elles vivent ici, et se traduisent en
/// DeliveryBrief. Le jour où il renomme un champ, la correction tient dans ce fichier.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class InternalEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapInternalEndpoints(this IEndpointRouteBuilder app)
    {
        var internals = app.MapGroup("/internal").WithTags("Internal").AllowAnonymous();

        internals.MapPost("/orders/ready-for-delivery", OrderReadyAsync)
            .WithName("InternalOrderReadyForDelivery");

        internals.MapPost("/orders/cancelled", OrderCancelledAsync)
            .WithName("InternalOrderCancelled");

        internals.MapPost("/couriers/standing-changed", CourierStandingChangedAsync)
            .WithName("InternalCourierStandingChanged");

        return app;
    }

    /// <summary>
    /// Une commande est prête : créer la course et lancer la première vague d'offres.
    ///
    /// RÉPOND 202, PAS 201. La course est bien créée de façon synchrone, mais ce que la
    /// marketplace veut savoir en rejouant un message, c'est « est-ce pris en compte » —
    /// pas « était-ce nouveau ». Un rejeu tombe sur la course existante et répond 202
    /// lui aussi : deux annonces, une course, aucun message en échec.
    /// </summary>
    private static async Task<IResult> OrderReadyAsync(
        HttpRequest http, DeliveryJobService dispatch, IConfiguration configuration, CancellationToken ct)
    {
        var (failure, payload) = await ReadSignedAsync<OrderReadyForDeliveryRequest>(http, configuration, ct);
        if (failure is not null)
        {
            return failure;
        }

        var request = payload!;

        // Copiés dans des locaux avant le contrôle : l'analyse de nullabilité suit un
        // local sans réserve, là où elle peut réinitialiser l'état d'une propriété.
        var pickups = request.Pickups;
        var dropoff = request.Dropoff;

        if (pickups is null || pickups.Count == 0)
        {
            return Results.BadRequest(new { error = "no_pickup" });
        }

        if (dropoff is null)
        {
            return Results.BadRequest(new { error = "no_dropoff" });
        }

        var brief = new DeliveryBrief(
            request.OrderId,
            request.ShippingFee,
            string.IsNullOrWhiteSpace(request.Currency) ? "XOF" : request.Currency,
            pickups.Select(ToBrief).ToList(),
            ToBrief(dropoff));

        var created = await dispatch.CreateFromBriefAsync(brief, ct);
        return created.Match(jobId => Results.Accepted($"/internal/jobs/{jobId.Value}", new { jobId = jobId.Value }));
    }

    /// <summary>La commande est annulée : la course tombe avec elle, le coursier est libéré.</summary>
    private static async Task<IResult> OrderCancelledAsync(
        HttpRequest http, JobProgressService progress, IConfiguration configuration, CancellationToken ct)
    {
        var (failure, payload) = await ReadSignedAsync<OrderCancelledRequest>(http, configuration, ct);
        if (failure is not null)
        {
            return failure;
        }

        var reason = string.IsNullOrWhiteSpace(payload!.Reason) ? "Commande annulée." : payload.Reason;
        var result = await progress.CancelByOrderAsync(payload.OrderId, reason, ct);
        return result.Match(Results.NoContent);
    }

    /// <summary>
    /// Le droit de travailler d'un coursier a changé — et c'est aussi ainsi qu'il ENTRE
    /// dans ce service.
    ///
    /// LA VALEUR EST RECOPIÉE, JAMAIS DÉDUITE. Ce service ne sait pas ce qu'est un
    /// dossier vérifié : seule la marketplace le sait. Lui laisser décider de son propre
    /// côté garantirait qu'ils divergent — et un coursier suspendu continuerait à
    /// recevoir des courses.
    /// </summary>
    private static async Task<IResult> CourierStandingChangedAsync(
        HttpRequest http, CourierService couriers, IConfiguration configuration, CancellationToken ct)
    {
        var (failure, payload) = await ReadSignedAsync<CourierStandingChangedRequest>(http, configuration, ct);
        if (failure is not null)
        {
            return failure;
        }

        var request = payload!;

        // Un statut inconnu est REFUSÉ, jamais interprété comme « actif ». Retomber sur
        // une valeur par défaut ferait rouler un coursier que la marketplace vient
        // peut-être de suspendre.
        if (!Enum.TryParse<CourierStanding>(request.Standing, ignoreCase: true, out var standing))
        {
            return Results.BadRequest(new { error = "unknown_standing", value = request.Standing });
        }

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? "Coursier" : request.DisplayName;
        var result = await couriers.ApplyStandingAsync(request.CourierId, displayName, standing, ct);
        return result.Match(Results.NoContent);
    }

    /// <summary>
    /// Lit le corps BRUT, vérifie la signature, puis désérialise.
    ///
    /// L'ORDRE EST OBLIGATOIRE. La signature porte sur les octets reçus : désérialiser
    /// d'abord puis re-sérialiser pour vérifier produirait une chaîne différente (ordre
    /// des champs, espaces, format des décimaux) et ferait échouer une signature pourtant
    /// valide.
    /// </summary>
    private static async Task<(IResult? Failure, TPayload? Payload)> ReadSignedAsync<TPayload>(
        HttpRequest http, IConfiguration configuration, CancellationToken ct)
        where TPayload : class
    {
        http.EnableBuffering();
        using var reader = new StreamReader(http.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        http.Body.Position = 0;

        var secret = configuration[InboundSignature.SecretKey];
        var signature = http.Headers[InboundSignature.Header].ToString();

        if (!InboundSignature.Verify(rawBody, signature, secret))
        {
            // 401 sans détail : dire POURQUOI la signature est refusée aiderait surtout
            // celui qui essaie d'en fabriquer une.
            return (Results.Unauthorized(), null);
        }

        try
        {
            var payload = JsonSerializer.Deserialize<TPayload>(rawBody, JsonOptions);
            return payload is null
                ? (Results.BadRequest(new { error = "invalid_payload" }), null)
                : (null, payload);
        }
        catch (JsonException)
        {
            return (Results.BadRequest(new { error = "invalid_payload" }), null);
        }
    }

    private static PickupBrief ToBrief(InboundPickup pickup)
        => new(
            pickup.ShipmentId, pickup.SellerId,
            string.IsNullOrWhiteSpace(pickup.SellerName) ? "Boutique" : pickup.SellerName,
            pickup.SellerPhone,
            pickup.CommuneCode ?? string.Empty,
            pickup.Quartier, pickup.Landmark, pickup.Line,
            pickup.Latitude, pickup.Longitude,
            (pickup.Parcels ?? new List<InboundParcel>())
                .Select(p => new ParcelBrief(p.Sku, p.Quantity)).ToList());

    private static DropoffBrief ToBrief(InboundDropoff dropoff)
        => new(
            dropoff.BuyerId, dropoff.Recipient, dropoff.Phone,
            dropoff.CommuneCode ?? string.Empty,
            dropoff.Quartier, dropoff.Landmark, dropoff.Line,
            dropoff.Latitude, dropoff.Longitude);

    // ─────────────────────────────────────────────────────────────────────────────
    // CHARGES ENTRANTES — la forme du fil, pas celle du domaine.
    //
    // Propriétés MUTABLES et NULLABLES, contrairement au reste du service : ce qui
    // arrive sur le réseau n'est pas encore validé. Un `required` ici ferait lever une
    // exception au milieu de la désérialisation, où l'on ne peut plus répondre un 400
    // propre. On accepte largement, puis on refuse explicitement.
    //
    // `LocationId` et `CommuneName` de l'événement du monolithe ne sont pas repris : le
    // dispatch ne s'en sert pas. Un champ ignoré ne fait pas échouer la lecture.
    // ─────────────────────────────────────────────────────────────────────────────

    public sealed class OrderReadyForDeliveryRequest
    {
        public Guid OrderId { get; set; }
        public decimal ShippingFee { get; set; }
        public string? Currency { get; set; }
        public List<InboundPickup>? Pickups { get; set; }
        public InboundDropoff? Dropoff { get; set; }
    }

    public sealed class InboundPickup
    {
        public Guid ShipmentId { get; set; }
        public Guid SellerId { get; set; }
        public string? SellerName { get; set; }
        public string? SellerPhone { get; set; }
        public string? CommuneCode { get; set; }
        public string? Quartier { get; set; }
        public string? Landmark { get; set; }
        public string? Line { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public List<InboundParcel>? Parcels { get; set; }
    }

    public sealed class InboundParcel
    {
        public string Sku { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    public sealed class InboundDropoff
    {
        public Guid BuyerId { get; set; }
        public string? Recipient { get; set; }
        public string? Phone { get; set; }
        public string? CommuneCode { get; set; }
        public string? Quartier { get; set; }
        public string? Landmark { get; set; }
        public string? Line { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    public sealed class OrderCancelledRequest
    {
        public Guid OrderId { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class CourierStandingChangedRequest
    {
        public Guid CourierId { get; set; }
        public string? DisplayName { get; set; }

        /// <summary>« Pending », « Active » ou « Suspended ». Toute autre valeur est refusée.</summary>
        public string? Standing { get; set; }
    }
}
