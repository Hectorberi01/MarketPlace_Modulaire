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

    /// <summary>
    /// Nombre maximal de caractères lus d'un corps à signer.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LIRE LE CORPS AVANT DE VÉRIFIER LA SIGNATURE EST OBLIGATOIRE. LE LIRE SANS
    /// LIMITE NE L'EST PAS.
    ///
    /// La signature porte sur les octets reçus : il FAUT donc les lire avant de pouvoir
    /// juger l'appel. La conséquence, jamais bornée jusqu'ici, est qu'un appelant
    /// anonyme faisait allouer une chaîne de la taille qu'il voulait — le seul plafond
    /// étant celui de Kestrel, 30 Mo. Ce service ne pose AUCUN limiteur de débit, nulle
    /// part : rien ne vient donc borner la répétition de l'appel non plus. Aucune limite
    /// de taille de corps n'existait nulle part dans le dépôt.
    ///
    /// Ces charges font quelques centaines d'octets. Soixante-quatre kilo-octets laissent
    /// une marge de deux ordres de grandeur et ramènent l'allocation à quelque chose
    /// qu'on peut se permettre de jeter.
    ///
    /// LE PLAFOND EST EN CARACTÈRES, pas en octets : on borne ce qu'on ALLOUE, ce qui est
    /// le point. Un corps UTF-8 peut valoir jusqu'à quatre octets par caractère, donc la
    /// borne réelle en octets est plus haute — sans importance ici, puisqu'on refuse bien
    /// avant d'atteindre quoi que ce soit de coûteux.
    /// </summary>
    private const int MaxSignedBodyChars = 64 * 1024;

    /// <summary>
    /// Lit le corps brut en refusant tout ce qui dépasse <see cref="MaxSignedBodyChars"/>.
    /// Renvoie <c>null</c> si le corps est trop grand — l'appelant répond alors 413.
    /// </summary>
    private static async Task<string?> ReadCappedBodyAsync(HttpRequest http, CancellationToken ct)
    {
        http.EnableBuffering();

        // L'en-tête annoncé permet de refuser sans rien lire quand il est présent et
        // honnête. Il ne fait pas foi — d'où le second contrôle, sur ce qu'on a lu.
        if (http.ContentLength is > MaxSignedBodyChars)
        {
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────
        // LE TAMPON SUIT LA TAILLE ANNONCÉE, ET NE LA CROIT PAS.
        //
        // Allouer les 64 Ki caractères quelle que soit la requête faisait coûter 128 Ko
        // au serveur pour un corps de zéro octet — la route de lecture, qui n'envoie
        // rien, est justement la plus économique à répéter pour un appelant.
        //
        // On dimensionne donc sur `ContentLength` quand il est là, et on garde le
        // plafond comme borne : un en-tête menteur n'agrandit rien, et le contrôle de
        // ce qui a été RÉELLEMENT lu, plus bas, reste le seul qui fasse foi.
        // ─────────────────────────────────────────────────────────────────────
        // Le `> 0` n'est pas décoratif : `ContentLength` est un `long?` librement
        // assignable par un intergiciel ou fabriqué en test, et une valeur négative
        // franchirait le plafond pour donner `new char[-4]`, soit une exception là où
        // on croyait borner une allocation.
        var taille = http.ContentLength is { } annoncee && annoncee is > 0 and < MaxSignedBodyChars
            ? (int)annoncee + 1
            : MaxSignedBodyChars + 1;

        var buffer = new char[taille];
        using var reader = new StreamReader(http.Body, leaveOpen: true);
        var read = await reader.ReadBlockAsync(buffer, ct);
        http.Body.Position = 0;

        // Comparé au PLAFOND, jamais à la taille du tampon : un corps plus gros que son
        // en-tête annonçait remplit simplement le tampon dimensionné dessus, ce qui donne
        // une signature fausse — et un refus en 401, qui est la bonne réponse.
        return read > MaxSignedBodyChars ? null : new string(buffer, 0, read);
    }


    public static IEndpointRouteBuilder MapInternalEndpoints(this IEndpointRouteBuilder app)
    {
        var internals = app.MapGroup("/internal").WithTags("Internal").AllowAnonymous();

        internals.MapPost("/orders/ready-for-delivery", OrderReadyAsync)
            .WithName("InternalOrderReadyForDelivery");

        internals.MapPost("/orders/cancelled", OrderCancelledAsync)
            .WithName("InternalOrderCancelled");

        internals.MapPost("/couriers/standing-changed", CourierStandingChangedAsync)
            .WithName("InternalCourierStandingChanged");

        // ═══════════════════════════════════════════════════════════════════════════
        // LA PREMIÈRE LECTURE DE CE CANAL, ET LA PREMIÈRE DU SERVICE.
        //
        // `/internal` ne portait que des annonces — la marketplace disait, le dispatch
        // écoutait. Celle-ci va dans le même sens (la marketplace appelle) mais demande
        // au lieu d'annoncer : la console d'administration a besoin de voir la flotte,
        // et rien, nulle part, ne rendait une position.
        //
        // POURQUOI PAS UNE ROUTE `/api/` AVEC UN JETON D'ADMIN. Le jeton d'administrateur
        // est cryptographiquement valide ici — la clé de signature est commune aux cinq
        // hôtes. Mais ce service ne connaît AUCUN rôle : il identifie ses appelants par
        // appartenance à la table `couriers`, et n'a pas de politique « Admin » à opposer
        // à un jeton. Lui en donner une reviendrait à y importer le modèle
        // d'autorisation du monolithe, pour une seule route.
        //
        // Le canal machine, lui, est déjà là et déjà fermé : signature du corps brut,
        // fenêtre de cinq minutes, secret distinct par sens. Le BFF Admin possède ce
        // secret et un `HttpClient` configuré — il ne manquait que la route.
        //
        // GET AVEC CORPS VIDE : la signature porte « horodatage.corps », et un corps vide
        // se signe comme un autre.
        //
        // CE QUE L'HORODATAGE FAIT, ET CE QU'IL NE FAIT PAS. Il ne supprime pas le rejeu,
        // il le borne à cinq minutes — `PartnerServiceSignature` le dit déjà. Sur une
        // LECTURE, rejouer est sans effet : on relit ce qu'on aurait pu lire.
        //
        // Le point qui mérite attention est ailleurs : la chaîne signée ne porte ni la
        // méthode ni le chemin. Cette route crée donc le premier couple (horodatage,
        // signature) valide sur un CORPS VIDE du système. Il ne mène aujourd'hui nulle
        // part — les trois POST refusent un corps vide en 400 à la désérialisation — mais
        // cette garantie tient à un détail de désérialisation, pas au contrôle de
        // signature. Le jour où une route `/internal` accepte un corps vide, il faudra
        // signer « horodatage.méthode chemin.corps » des deux côtés, ensemble.
        // ═══════════════════════════════════════════════════════════════════════════
        internals.MapGet("/couriers/positions", CourierPositionsAsync)
            .WithName("InternalCourierPositions");

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

    /// <summary>
    /// Où est la flotte, à l'instant.
    ///
    /// Ne renvoie QUE les coursiers actifs, en ligne ou en course, et porteurs d'une
    /// position. Les hors ligne sont écartés en base : leur dernière position date de
    /// leur dernière connexion, et la dessiner ferait croire à une présence.
    ///
    /// Chaque ligne porte l'ÂGE de sa position, et la réponse porte les DEUX SEUILS qui
    /// disent comment le lire — celui des offres et celui des actions, qui ne valent pas
    /// la même chose. Un repère sur une carte a la même allure qu'il date de dix secondes
    /// ou de deux heures ; sans ces trois valeurs, la console affirmerait une présence que
    /// ce service ne croit déjà plus. Voir <see cref="FleetView"/>, qui les documente.
    /// </summary>
    private static async Task<IResult> CourierPositionsAsync(
        HttpRequest http, FleetQuery fleet, IConfiguration configuration, CancellationToken ct)
    {
        var refus = await VerifySignatureAsync(http, configuration, ct);
        if (refus is not null)
        {
            return refus;
        }

        return Results.Ok(await fleet.ListOnFieldAsync(ct));
    }

    /// <summary>
    /// Vérifie la signature SANS attendre de charge utile.
    ///
    /// `ReadSignedAsync` désérialise et refuse un corps vide — ce qui est juste pour les
    /// trois annonces, et faux pour une lecture. Les deux partagent donc le contrôle de
    /// signature, et lui seul.
    /// </summary>
    private static async Task<IResult?> VerifySignatureAsync(
        HttpRequest http, IConfiguration configuration, CancellationToken ct)
    {
        var rawBody = await ReadCappedBodyAsync(http, ct);
        if (rawBody is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var secret = configuration[InboundSignature.SecretKey];
        var signature = http.Headers[InboundSignature.Header].ToString();
        var timestamp = http.Headers[InboundSignature.TimestampHeader].ToString();

        return InboundSignature.Verify(rawBody, timestamp, signature, secret)
            ? null
            : Results.Unauthorized();
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

        // L'HORODATAGE D'ÉMISSION EST OBLIGATOIRE, ET SON ABSENCE EST REFUSÉE.
        //
        // Retomber sur « maintenant » ferait passer un vieux message rejoué pour la
        // décision la plus récente — exactement ce que le verrou monotone doit empêcher.
        // Retomber sur DateTime.MinValue ferait l'inverse : toute annonce serait périmée,
        // et plus aucun coursier ne changerait de statut. Ni l'un ni l'autre : on refuse.
        if (request.OccurredOnUtc is null)
        {
            return Results.BadRequest(new { error = "missing_occurred_at" });
        }

        var displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? "Coursier" : request.DisplayName;
        var result = await couriers.ApplyStandingAsync(
            request.CourierId, request.UserId, displayName, standing,
            request.OccurredOnUtc.Value.ToUniversalTime(), ct);
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
        var rawBody = await ReadCappedBodyAsync(http, ct);
        if (rawBody is null)
        {
            return (Results.StatusCode(StatusCodes.Status413PayloadTooLarge), null);
        }

        var secret = configuration[InboundSignature.SecretKey];
        var signature = http.Headers[InboundSignature.Header].ToString();
        var timestamp = http.Headers[InboundSignature.TimestampHeader].ToString();

        if (!InboundSignature.Verify(rawBody, timestamp, signature, secret))
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
        /// <summary>Identifiant de BÉNÉFICIAIRE — celui qui désigne le portefeuille à créditer.</summary>
        public Guid CourierId { get; set; }

        /// <summary>
        /// Identifiant du COMPTE — celui que porte le jeton JWT du coursier.
        ///
        /// Les deux sont nécessaires, et ce sont deux Guid différents : l'un fait payer,
        /// l'autre authentifie. Voir Courier.UserId. N'exiger que le premier donnait un
        /// coursier enregistré, offert des courses, et incapable d'en prendre une.
        /// </summary>
        public Guid UserId { get; set; }

        public string? DisplayName { get; set; }

        /// <summary>« Pending », « Active » ou « Suspended ». Toute autre valeur est refusée.</summary>
        public string? Standing { get; set; }

        /// <summary>
        /// Instant où la MARKETPLACE a pris la décision — pas celui où l'appel arrive.
        ///
        /// C'est ce qui permet d'ordonner deux annonces contradictoires sans se fier à
        /// l'ordre d'arrivée, qu'un rejeu ou un réessai d'outbox rend peu fiable. Voir
        /// Courier.ApplyStanding. Nullable pour pouvoir répondre un 400 propre : un
        /// `required` ferait lever au milieu de la désérialisation.
        /// </summary>
        public DateTime? OccurredOnUtc { get; set; }
    }
}
