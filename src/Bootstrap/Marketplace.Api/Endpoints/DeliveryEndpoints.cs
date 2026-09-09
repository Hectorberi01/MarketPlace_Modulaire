using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Configuration;
using Marketplace.Api.Extensions;
using Marketplace.BuildingBlocks.Infrastructure.Security;
using Marketplace.Modules.Settlement.Application.Wallets;
using Marketplace.Modules.Shipping.Application.Shipments.Commands;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// API — L'ENTRÉE DU SERVICE COURSIER.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// UNE SEULE ROUTE, ET ELLE FAIT SORTIR DE L'ARGENT.
///
/// Le service de dispatch est déployé à part (docs/CDC_Service_Livraison_Coursier.md) :
/// il tient les courses, les positions et le temps réel, et la marketplace n'en sait
/// rien. Quand une course est livrée, il l'annonce ici, avec la rémunération qu'il a
/// calculée — c'est lui qui connaît la distance et les points de ramassage.
///
/// Cela veut dire, très concrètement, QUE L'APPELANT DE CETTE ROUTE DÉCIDE COMBIEN SORT
/// DU PORTEFEUILLE DE LA PLATEFORME. D'où trois protections, et aucune n'est
/// décorative :
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

        return app;
    }

    /// <summary>
    /// Une course a été livrée : crédite le coursier de la rémunération annoncée.
    /// </summary>
    private static async Task<IResult> CourseCompletedAsync(
        HttpRequest http, ISender sender, IConfiguration configuration, CancellationToken ct)
    {
        // Le corps est lu BRUT : la signature porte sur les octets reçus, pas sur un
        // objet re-sérialisé. Désérialiser d'abord puis re-sérialiser pour vérifier
        // produirait une chaîne différente (ordre des champs, espaces) et ferait échouer
        // une signature pourtant valide.
        var rawBody = await ReadCappedBodyAsync(http, ct);
        if (rawBody is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var secret = configuration[SecretKey];
        var signature = http.Headers[SignatureHeader].ToString();
        var timestamp = http.Headers[PartnerServiceSignature.TimestampHeader].ToString();

        // Signature ET fraîcheur. Un corps signé capturé une fois — journal, proxy,
        // dépôt de logs — restait payable indéfiniment tant que rien n'horodatait
        // l'appel. Cette route fait sortir de l'argent : c'est la moins bien placée
        // pour accepter un message dont on ne sait pas quand il a été émis.
        if (!PartnerServiceSignature.Verify(rawBody, timestamp, signature, secret))
        {
            // 401 sans détail : dire POURQUOI la signature est refusée aiderait surtout
            // celui qui essaie d'en fabriquer une.
            return Results.Unauthorized();
        }

        CourseCompletedRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<CourseCompletedRequest>(
                rawBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "invalid_payload" });
        }

        if (request is null)
        {
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
}
