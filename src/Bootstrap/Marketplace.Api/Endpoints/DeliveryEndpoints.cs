using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Configuration;
using Marketplace.Api.Extensions;
using Marketplace.BuildingBlocks.Infrastructure.Security;
using Marketplace.Modules.Delivery.Contracts;
using Marketplace.Modules.Notifications.Application.Abstractions;
using Marketplace.Modules.Notifications.Domain.Devices;
using Marketplace.Modules.Settlement.Application.Wallets;
using Marketplace.Modules.Shipping.Application.Shipments.Commands;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// API — L'ENTRÉE DU SERVICE COURSIER.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// DEUX ROUTES : LES DEUX ISSUES D'UNE COURSE. L'UNE FAIT SORTIR DE L'ARGENT.
///
/// Le service de dispatch est déployé à part (docs/CDC_Service_Livraison_Coursier.md) :
/// il tient les courses, les positions et le temps réel, et la marketplace n'en sait
/// rien. Quand une course est livrée, il l'annonce ici, avec la rémunération qu'il a
/// calculée — c'est lui qui connaît la distance et les points de ramassage. Quand elle
/// ÉCHOUE, il l'annonce aussi : sans quoi l'expédition reste « en préparation » pour
/// toujours et personne, pas même le vendeur qui détient le colis, ne l'apprend.
///
/// LES DEUX ANNONCES PASSENT PAR LE MÊME DISPOSITIF, MAIS N'ENGAGENT PAS LA MÊME CHOSE.
/// « Livrée » décide combien sort du portefeuille de la plateforme ; « en échec » ne
/// porte AUCUN montant et ne crédite rien — aucune livraison n'a eu lieu. Ce qui suit
/// vaut donc pleinement pour la première, et reste exigé de la seconde parce qu'un
/// faux échec est aussi une attaque : il fait notifier des vendeurs et immobilise des
/// commandes payées.
///
/// D'où trois protections, et aucune n'est décorative :
///
///  • SIGNATURE HMAC obligatoire, fermée par défaut. Pas de secret configuré en
///    production = appel rejeté. Un webhook non signé qui crédite un portefeuille,
///    c'est un distributeur de billets en libre-service.
///  • IDEMPOTENCE par identifiant de course, côté commande. Le réseau tombe, le
///    service rejoue : la course ne doit être payée qu'une fois, et il n'y a aucun
///    moyen de reprendre de l'argent déjà retiré sur un Mobile Money.
///  • STATUT DU COURSIER vérifié : un coursier non vérifié ou suspendu ne reçoit pas
///    de nouveau crédit.
///
/// CE QUI MANQUE ENCORE, ET QU'IL FAUDRA POSER : un PLAFOND. Rien ici ne dit qu'une
/// course ne peut pas valoir 900 000. Le jour où la règle de tarification sera arrêtée,
/// c'est le premier contrôle à ajouter.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class DeliveryEndpoints
{
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
    /// étant celui de Kestrel, 30 Mo, et ces routes ont en plus le limiteur de débit
    /// désactivé. Aucune limite de taille de corps n'existait nulle part dans le dépôt.
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

        var buffer = new char[MaxSignedBodyChars + 1];
        using var reader = new StreamReader(http.Body, leaveOpen: true);
        var read = await reader.ReadBlockAsync(buffer, ct);
        http.Body.Position = 0;

        return read > MaxSignedBodyChars ? null : new string(buffer, 0, read);
    }

    /// <summary>En-tête portant la signature HMAC du corps brut.</summary>
    private const string SignatureHeader = "X-Delivery-Signature";

    /// <summary>Emplacement du secret partagé avec le service coursier.</summary>
    private const string SecretKey = "Delivery:WebhookSecret";

    public static IEndpointRouteBuilder MapDeliveryEndpoints(this IEndpointRouteBuilder app)
    {
        // MapGroup NU, EN CONNAISSANCE DE CAUSE. La règle d'ApiAuthorization veut que
        // tout groupe parte de MapAdminGroup ou MapAuthenticatedGroup ; l'exception est
        // la même que pour les webhooks PSP : l'appelant est un SERVICE, il n'a pas de
        // jeton, et son authentification est la SIGNATURE du corps — vérifiée ci-dessous,
        // fermée par défaut. Un groupe authentifié rejetterait l'appel avant même qu'on
        // puisse la contrôler.
        //
        // DisableRateLimiting pour la même raison qu'eux : le limiteur global partitionne
        // les anonymes par adresse IP, et tous les appels du service coursier viennent
        // d'une poignée d'adresses. Les limiter ferait échouer les paiements de courses
        // au pire moment — un pic de livraisons.
        var delivery = app.MapGroup("/api/delivery").WithTags("Delivery").DisableRateLimiting();

        delivery.MapPost("/courses/completed", CourseCompletedAsync)
            .WithName("DeliveryCourseCompleted")
            .AllowAnonymous();

        // L'AUTRE ISSUE. Cette route N'EXISTAIT PAS alors que le service coursier l'appelle :
        // le drain recevait des 404, réessayait dix fois, puis abandonnait le message. La
        // marketplace n'apprenait donc JAMAIS un échec de course, et l'expédition restait
        // « en préparation » indéfiniment — sur une commande déjà payée.
        delivery.MapPost("/courses/failed", CourseFailedAsync)
            .WithName("DeliveryCourseFailed")
            .AllowAnonymous();

        // ═════════════════════════════════════════════════════════════════════════
        // RÉVEILLER LE COURSIER, TÉLÉPHONE RANGÉ.
        //
        // Le dispatch tient le hub SignalR : il couvre l'application OUVERTE. Une offre
        // vit vingt-cinq secondes ; un coursier qui a remis son téléphone dans sa poche
        // ne la voyait donc jamais, et la course partait à celui qui regardait son
        // écran au bon moment.
        //
        // POURQUOI CETTE ROUTE PLUTÔT QU'UN ENVOI DEPUIS LE DISPATCH. Les jetons
        // d'appareil, les préférences et le compte de service FCM vivent ICI. Le
        // compose du dispatch va jusqu'à ANNULER le montage du compte de service —
        // « ce service n'envoie aucune notification push » est une décision écrite, pas
        // un oubli. On la respecte : le dispatch annonce le fait, la marketplace le
        // pousse.
        // ═════════════════════════════════════════════════════════════════════════
        delivery.MapPost("/couriers/offer", CourierOfferAsync)
            .WithName("DeliveryCourierOffer")
            .AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Lit, AUTHENTIFIE et désérialise une annonce du service coursier. Rend soit la
    /// réponse d'échec à renvoyer telle quelle, soit la charge utile.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// UN SEUL EXEMPLAIRE DU DISPOSITIF, PARCE QUE DEUX EXEMPLAIRES DÉRIVENT.
    ///
    /// Ce préambule était écrit dans le corps de l'unique route. À la deuxième, le
    /// recopier aurait créé deux versions d'une vérification de signature — et la
    /// question n'est pas de savoir SI elles divergent, mais quand : on ajoute une
    /// fenêtre de fraîcheur ici, un contrôle de longueur là, et la route recopiée reste
    /// en arrière. Une faiblesse de ce genre ne se voit à la lecture d'AUCUN des deux
    /// fichiers, puisque chacun paraît complet.
    ///
    /// MÊME SECRET, MÊME EN-TÊTE, MÊME PLAFOND, MÊME 401 SANS DÉTAIL : ce n'est pas une
    /// commodité d'écriture, c'est la garantie que les deux routes ne peuvent pas être
    /// protégées différemment.
    ///
    /// Le corps est lu BRUT : la signature porte sur les octets reçus, pas sur un objet
    /// re-sérialisé. Désérialiser d'abord puis re-sérialiser pour vérifier produirait une
    /// chaîne différente (ordre des champs, espaces) et ferait échouer une signature
    /// pourtant valide.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    /// <returns>
    /// Exactement l'un des deux est non nul. Le compilateur ne peut pas l'exprimer, d'où
    /// la double garde chez les appelants.
    /// </returns>
    private static async Task<(IResult? Rejection, TRequest? Request)> ReadSignedAsync<TRequest>(
        HttpRequest http, IConfiguration configuration, CancellationToken ct)
        where TRequest : class
    {
        var rawBody = await ReadCappedBodyAsync(http, ct);
        if (rawBody is null)
        {
            return (Results.StatusCode(StatusCodes.Status413PayloadTooLarge), null);
        }

        var secret = configuration[SecretKey];
        var signature = http.Headers[SignatureHeader].ToString();
        var timestamp = http.Headers[PartnerServiceSignature.TimestampHeader].ToString();

        // Signature ET fraîcheur. Un corps signé capturé une fois — journal, proxy,
        // dépôt de logs — restait payable indéfiniment tant que rien n'horodatait
        // l'appel. Ces routes font sortir de l'argent ou immobilisent une commande
        // payée : ce sont les moins bien placées pour accepter un message dont on ne
        // sait pas quand il a été émis.
        if (!PartnerServiceSignature.Verify(rawBody, timestamp, signature, secret))
        {
            // 401 sans détail : dire POURQUOI la signature est refusée aiderait surtout
            // celui qui essaie d'en fabriquer une.
            return (Results.Unauthorized(), null);
        }

        TRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<TRequest>(
                rawBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return (Results.BadRequest(new { error = "invalid_payload" }), null);
        }

        return request is null
            ? (Results.BadRequest(new { error = "invalid_payload" }), null)
            : (null, request);
    }

    /// <summary>
    /// Une offre de course vient d'être proposée : réveille le téléphone du coursier.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// PUSH SEUL, SANS NOTIFICATION IN-APP — ET C'EST LE POINT DÉLICAT.
    ///
    /// `NotificationDispatcher.NotifyAsync` aurait été le chemin le plus court, mais il
    /// PERSISTE une notification à chaque appel. Une offre est éphémère et nombreuse :
    /// un coursier peut en recevoir des dizaines par jour, dont la plupart expirent en
    /// vingt-cinq secondes. On aurait rempli sa liste de notifications de courses qui
    /// n'existent plus — et l'application livreur n'a d'ailleurs aucun écran pour la
    /// lire. On pousse donc directement, sans trace persistante.
    ///
    /// BEST-EFFORT, COMME LE HUB. L'offre existe en base dès sa création : elle reste
    /// acceptable par `GET /api/courier/offers`. Un échec d'envoi ne doit donc jamais
    /// faire échouer l'annonce — on répond 204 même sans jeton d'appareil, sans quoi le
    /// dispatch journaliserait une erreur pour un coursier qui n'a tout simplement pas
    /// installé l'application.
    ///
    /// AUCUN DÉTAIL DE COURSE DANS LA CHARGE UTILE. Le titre et le corps sont écrits
    /// ici, et la donnée transportée se limite au `jobId` : une notification s'affiche
    /// sur un écran verrouillé, et l'adresse du client n'a rien à y faire. L'application
    /// va chercher le détail avec son jeton, quand le coursier ouvre.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> CourierOfferAsync(
        HttpRequest http,
        IConfiguration configuration,
        IDeliveryModuleApi couriers,
        IDeviceTokenRepository devices,
        IPushSender push,
        CancellationToken ct)
    {
        var (rejection, request) = await ReadSignedAsync<CourierOfferRequest>(http, configuration, ct);
        if (rejection is not null)
        {
            return rejection;
        }

        if (request is null)
        {
            return Results.BadRequest(new { error = "invalid_payload" });
        }

        // Du COURSIER au COMPTE : les jetons d'appareil sont rangés par utilisateur,
        // et le dispatch ne connaît que l'identifiant coursier.
        var courier = await couriers.GetCourierAsync(request.CourierId, ct);
        if (courier is null)
        {
            // 204 et non 404 : un coursier inconnu ici signale une désynchronisation
            // entre les deux services, pas un appel malformé. Le dispatch n'a rien à
            // rejouer — l'offre, elle, est déjà valable de son côté.
            return Results.NoContent();
        }

        var tokens = await devices.ListByUserAsync(courier.UserId, ct);
        if (tokens.Count == 0)
        {
            return Results.NoContent();
        }

        var resultat = await push.SendAsync(
            tokens.Select(t => t.Token).ToList(),
            new PushMessage(
                "Nouvelle course proposée",
                "Ouvrez l'application pour l'accepter avant qu'elle n'expire.",
                new Dictionary<string, string>
                {
                    ["type"] = "courier_offer",
                    ["jobId"] = request.DeliveryJobId.ToString(),
                }),
            ct);

        // Purge des jetons que FCM déclare morts : sans elle, un téléphone réinstallé
        // laisse derrière lui un jeton qu'on réessaie à chaque offre, indéfiniment.
        if (resultat.InvalidTokens.Count > 0)
        {
            await devices.RemoveByTokensAsync(resultat.InvalidTokens, ct);
        }

        return Results.NoContent();
    }

    /// <summary>
    /// Une course a été livrée : crédite le coursier de la rémunération annoncée.
    /// </summary>
    private static async Task<IResult> CourseCompletedAsync(
        HttpRequest http, ISender sender, IConfiguration configuration, CancellationToken ct)
    {
        var (rejection, request) = await ReadSignedAsync<CourseCompletedRequest>(http, configuration, ct);
        if (rejection is not null)
        {
            return rejection;
        }

        if (request is null)
        {
            // Inatteignable : ReadSignedAsync rend une réponse dès qu'il n'y a pas de
            // charge. La garde est là pour que le contrat reste vrai si cette méthode
            // change un jour, plutôt que de le supposer avec un « ! ».
            return Results.BadRequest(new { error = "invalid_payload" });
        }

        // ═════════════════════════════════════════════════════════════════════════
        // ON ENREGISTRE LA LIVRAISON AVANT DE PAYER, ET L'ORDRE EST LE POINT.
        //
        // Cette route ne faisait QUE créditer le coursier. Elle ne touchait ni Shipping
        // ni Ordering, et rien d'autre ne le faisait à sa place : l'expédition restait
        // « en préparation », la commande n'atteignait jamais « livrée », et la séquestre
        // du vendeur n'était jamais libérée. Le coursier était payé pour une livraison
        // que le système n'enregistrait nulle part.
        //
        // LA LIVRAISON EST UN FAIT, LE PAIEMENT EN EST LA CONSÉQUENCE. Enregistrer
        // d'abord garantit qu'un coursier qu'on ne peut pas payer — suspendu, plafond
        // atteint, portefeuille indisponible — ne fait pas disparaître le fait qu'il a
        // déposé le colis. L'inverse perdrait la livraison pour une raison comptable.
        //
        // DEUX MODULES, DEUX TRANSACTIONS, ET C'EST ACCEPTABLE ICI parce que les deux
        // commandes sont idempotentes : `MarkDeliveredByCourier` sur l'identifiant de
        // course, `CreditCourierDeliveryCommand` sur une écriture de grand livre. Si le
        // crédit échoue, on répond en erreur, la file de sortie du dispatch rejoue
        // l'appel entier, et la première commande ne fait rien la seconde fois.
        // ═════════════════════════════════════════════════════════════════════════
        var delivered = await sender.Send(
            new MarkOrderDeliveredByCourierCommand(request.OrderId, request.DeliveryJobId), ct);

        if (delivered.IsFailure)
        {
            return delivered.Match(() => Results.NoContent());
        }

        var result = await sender.Send(
            new CreditCourierDeliveryCommand(
                request.CourierId, request.OrderId, request.DeliveryJobId,
                request.Amount, string.IsNullOrWhiteSpace(request.Currency) ? "XOF" : request.Currency),
            ct);

        return result.Match(() => Results.NoContent());
    }

    /// <summary>
    /// Une course a ÉCHOUÉ : le colis n'est jamais parti. On le consigne sur les
    /// expéditions concernées et on prévient le vendeur. ON NE CRÉDITE RIEN.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// CE QUE LA MARKETPLACE FAIT DE CETTE ANNONCE, ET POURQUOI PAS AUTRE CHOSE.
    ///
    /// Une seule commande est envoyée, et elle n'engage aucun mouvement d'argent. Trois
    /// décisions la caractérisent :
    ///
    ///  • ON NE PAIE PAS LE COURSIER. Aucune livraison n'a eu lieu ; l'annonce ne porte
    ///    d'ailleurs aucun montant, précisément pour qu'aucun code de ce côté-ci ne
    ///    puisse en déduire un paiement. Un éventuel dédommagement de course tentée est
    ///    une décision d'exploitation, pas une conséquence de webhook.
    ///
    ///  • L'EXPÉDITION RESTE « EN PRÉPARATION ». C'est l'état d'où le vendeur peut
    ///    repartir : `Shipment.MarkShipped` l'accepte et confie la part à un transporteur
    ///    tiers. La faire avancer ou l'annuler condamnerait une commande DÉJÀ PAYÉE.
    ///
    ///  • LE VENDEUR EST PRÉVENU, par événement d'intégration consommé par
    ///    Notifications. C'est le seul point qui rend la correction utile : lui seul
    ///    détient le colis et peut le faire repartir.
    ///
    /// IDEMPOTENCE, PARCE QUE LE DRAIN REJOUE JUSQU'À DIX FOIS. Elle est portée par
    /// l'identifiant de course, à deux endroits : sur l'expédition
    /// (`Shipment.FailedDeliveryJobId`), qui absorbe les rejeux de CET appel, et sur la
    /// notification (`NotifyOnceAsync`), qui absorbe ceux de l'outbox — lesquels
    /// surviennent après la transaction et échappent donc au premier verrou.
    ///
    /// 204 EN CAS DE SUCCÈS, comme la livraison : le drain arrête de réessayer. Un échec
    /// répond en erreur pour qu'il réessaie, puis mette en lettre morte — et la lettre
    /// morte est journalisée en Critical par l'outbox du service coursier.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> CourseFailedAsync(
        HttpRequest http, ISender sender, IConfiguration configuration, CancellationToken ct)
    {
        var (rejection, request) = await ReadSignedAsync<CourseFailedRequest>(http, configuration, ct);
        if (rejection is not null)
        {
            return rejection;
        }

        if (request is null)
        {
            return Results.BadRequest(new { error = "invalid_payload" });
        }

        var result = await sender.Send(
            new MarkOrderCourierDeliveryFailedCommand(
                request.CourierId,
                request.OrderId,
                request.DeliveryJobId,
                // Le motif est normalisé par l'agrégat, pas ici : un motif absent ne doit
                // JAMAIS empêcher l'enregistrement de l'échec, sinon on retombe dans le
                // silence qu'on corrige.
                request.Reason ?? string.Empty,
                request.FailedAtUtc),
            ct);

        return result.Match(() => Results.NoContent());
    }

    /// <summary>
    /// Annonce d'une offre proposée. Miroir du `CourierOfferPayload` du service de
    /// dispatch — CONTRAT ENTRE DEUX DÉPLOIEMENTS, à changer des deux côtés à la fois.
    /// </summary>
    /// <param name="CourierId">À qui l'offre est faite. Résolu ici en compte utilisateur.</param>
    /// <param name="DeliveryJobId">
    /// La course proposée. Transportée dans la notification pour que l'ouverture mène
    /// directement à elle — et c'est la SEULE donnée de course qui voyage.
    /// </param>
    public sealed record CourierOfferRequest(Guid CourierId, Guid DeliveryJobId);

    /// <summary>
    /// Annonce d'une course livrée.
    /// </summary>
    /// <param name="DeliveryJobId">
    /// Identifiant de la course chez le service de dispatch. C'est LA CLÉ D'IDEMPOTENCE :
    /// deux annonces portant le même identifiant ne paient qu'une fois.
    /// </param>
    /// <param name="Amount">
    /// Rémunération du coursier, calculée par le service de dispatch — la marketplace ne
    /// la recalcule pas, elle ne connaît ni la distance ni les points de ramassage.
    /// </param>
    public sealed record CourseCompletedRequest(
        Guid CourierId,
        Guid OrderId,
        Guid DeliveryJobId,
        decimal Amount,
        string? Currency = null);

    /// <summary>
    /// Annonce d'une course en échec. Miroir exact de `CourierDeliveryFailedPayload` du
    /// service de dispatch — c'est un CONTRAT ENTRE DEUX DÉPLOIEMENTS, et il ne se change
    /// que des deux côtés à la fois.
    /// </summary>
    /// <param name="CourierId">
    /// Qui a renoncé. Ne décide de rien ici : il n'est pas payé, et il n'est pas
    /// sanctionné par une route. Il est journalisé, sans quoi un coursier qui abandonne
    /// systématiquement ses courses n'apparaîtrait nulle part.
    /// </param>
    /// <param name="OrderId">
    /// LA COMMANDE À DÉBLOQUER. C'est par elle qu'on retrouve les expéditions figées :
    /// l'annonce ne porte aucun identifiant d'expédition, parce qu'une course couvre la
    /// commande entière.
    /// </param>
    /// <param name="DeliveryJobId">
    /// LA CLÉ D'IDEMPOTENCE, comme pour la livraison : deux annonces du même échec ne
    /// produisent qu'un seul traitement et une seule notification vendeur.
    /// </param>
    /// <param name="Reason">
    /// Le motif tel que la course le porte, déjà normalisé par le service de dispatch.
    /// Nullable ici parce qu'un corps JSON peut toujours l'omettre : c'est l'agrégat qui
    /// garantit un libellé exploitable, pas la confiance dans l'appelant.
    /// </param>
    /// <param name="FailedAtUtc">Instant de l'abandon, daté par le service de dispatch.</param>
    public sealed record CourseFailedRequest(
        Guid CourierId,
        Guid OrderId,
        Guid DeliveryJobId,
        string? Reason,
        DateTime FailedAtUtc);
}
