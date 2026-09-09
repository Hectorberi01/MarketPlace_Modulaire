using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;
using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Payments.Application.Payments.Commands;
using Marketplace.Modules.Payments.Application.Payments.Commands.InitiatePayment;
using Marketplace.Modules.Payments.Application.Payments.Queries;
using Marketplace.Modules.Ordering.Application.Orders.Queries;
using Marketplace.Modules.Payments.Contracts;
using Marketplace.Modules.Settlement.Application.Wallets;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Payments. Initiation via PSP (Stripe / PayPal) en
/// checkout hébergé ou intention serveur ; confirmation par webhook signé ou au
/// retour de redirection. La capture déclenche la confirmation de commande,
/// l'échec son annulation (via events d'intégration).
/// </summary>
public static class PaymentsEndpoints
{
    /// <summary>Enregistre les routes de paiement.</summary>
    public static IEndpointRouteBuilder MapPaymentsEndpoints(this IEndpointRouteBuilder app)
    {
        var payments = app.MapAuthenticatedGroup("/api/payments").WithTags("Payments");

        payments.MapPost("/", InitiateAsync).WithName("InitiatePayment");
        payments.MapGet("/{id:guid}", GetAsync).WithName("GetPayment");
        payments.MapGet("/order/{orderId:guid}", GetByOrderAsync).WithName("GetPaymentByOrder");
        // Capture / échec / remboursement : actions sensibles simulant le PSP, réservées Admin.
        // La capture confirme la commande et solde le stock : accessible à tout compte
        // authentifié (avec une référence PSP fournie par le client), elle permettrait de
        // marquer une commande « payée » sans paiement réel. Les captures légitimes passent
        // par les webhooks signés ou le retour de redirection (qui réinterroge le PSP).
        payments.MapPost("/{id:guid}/capture", CaptureAsync).WithName("CapturePayment").RequireAuthorization(p => p.RequireRole("Admin"));
        payments.MapPost("/{id:guid}/fail", FailAsync).WithName("FailPayment").RequireAuthorization(p => p.RequireRole("Admin"));
        payments.MapPost("/{id:guid}/refund", RefundAsync).WithName("RefundPayment").RequireAuthorization(p => p.RequireRole("Admin"));

        // Webhooks PSP + retour de redirection : appelés par le PSP / le navigateur, donc anonymes.
        //
        // ═════════════════════════════════════════════════════════════════════════
        // `DisableRateLimiting()` — ET C'EST INDISPENSABLE.
        //
        // Le limiteur global partitionne les appelants anonymes PAR ADRESSE IP. Or TOUS
        // les webhooks d'un PSP arrivent de la MÊME poignée d'adresses : ils tomberaient
        // donc dans une seule et unique partition.
        //
        // Un pic de commandes — exactement le moment où l'on a le moins envie de
        // problèmes — dépasserait le quota, et FedaPay recevrait des 429. Résultat :
        // des paiements ENCAISSÉS chez le PSP mais JAMAIS confirmés chez nous. Des
        // clients débités dont la commande reste « en attente de paiement ».
        //
        // Ces routes ne sont pas pour autant sans défense : leur SIGNATURE est vérifiée
        // (HMAC, comparaison en temps constant, fail-closed en production). Un appelant
        // qui ne sait pas signer ne peut rien faire — le limiteur n'ajouterait rien
        // contre lui, et pénaliserait le PSP légitime.
        //
        // Le débit brut reste borné à l'edge par Traefik.
        // ═════════════════════════════════════════════════════════════════════════
        var gateway = app.MapGroup("/api/payments").WithTags("Payments").DisableRateLimiting();
        gateway.MapPost("/webhooks/stripe", StripeWebhookAsync).WithName("StripeWebhook").AllowAnonymous();
        gateway.MapPost("/webhooks/paypal", PayPalWebhookAsync).WithName("PayPalWebhook").AllowAnonymous();
        gateway.MapPost("/webhooks/momo", MomoCallbackAsync).WithName("MomoCallback").AllowAnonymous();
        gateway.MapPost("/webhooks/moov", MoovCallbackAsync).WithName("MoovCallback").AllowAnonymous();
        gateway.MapPost("/webhooks/fedapay", FedaPayWebhookAsync).WithName("FedaPayWebhook").AllowAnonymous();
        gateway.MapGet("/return/{id:guid}", ReturnAsync).WithName("ConfirmPaymentReturn").AllowAnonymous();

        return app;
    }

    /// <summary>Initie un paiement et renvoie l'URL de redirection (checkout) ou le client secret (intent).</summary>
    private static async Task<IResult> InitiateAsync(
        ClaimsPrincipal principal, InitiateRequest request, ISender sender, CancellationToken ct)
    {
        // ═════════════════════════════════════════════════════════════════════════
        // L'IDENTIFIANT DE COMMANDE VIENT DU CORPS : IL FAUT DONC VÉRIFIER QU'ELLE EST
        // BIEN CELLE DE L'APPELANT.
        //
        // Les deux routes voisines — GET /{id} et GET /order/{orderId} — font ce
        // contrôle ; celle-ci ne le faisait pas. Trois conséquences, dont la première
        // est la plus vicieuse :
        //
        //  • BLOCAGE DÉFINITIF DU PAIEMENT D'AUTRUI. Un paiement « Pending » créé sur
        //    la commande d'un tiers fait échouer sa vraie tentative avec
        //    « payments.already_exists ». Sa commande devient impayable, et il n'a
        //    aucun moyen de comprendre pourquoi.
        //  • ORACLE D'ÉNUMÉRATION : les réponses distinguent « introuvable »,
        //    « non payable » et « déjà existant », ce qui permet de cartographier les
        //    commandes de la plateforme.
        //  • URL DE RETOUR CHOISIE PAR L'ATTAQUANT sur une session PSP rattachée à
        //    quelqu'un d'autre.
        // ═════════════════════════════════════════════════════════════════════════
        var order = await sender.Send(new GetOrderQuery(request.OrderId), ct);
        if (order.IsFailure)
        {
            return Results.NotFound();
        }

        if (!principal.IsInRole("Admin") && CurrentUserId(principal) != order.Value.BuyerId)
        {
            // 404, et non 403 : répondre « interdit » confirmerait que la commande
            // existe, ce qui suffit à l'énumération.
            return Results.NotFound();
        }

        var command = new InitiatePaymentCommand(
            request.OrderId, request.Method, request.Provider,
            request.Flow ?? "HostedCheckout", request.ReturnUrl, request.CancelUrl, request.PayerPhone);
        var result = await sender.Send(command, ct);
        return result.Match(r => Results.Ok(r));
    }

    /// <summary>Récupère un paiement par son identifiant (propriétaire ou admin).</summary>
    private static async Task<IResult> GetAsync(ClaimsPrincipal principal, Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetPaymentQuery(id), ct);
        if (result.IsFailure)
        {
            // LES DEUX BRANCHES RÉPONDENT LA MÊME CHOSE, ET C'EST TOUT L'OBJET.
            //
            // Celle-ci rendait un ProblemDetails avec le code d'erreur dans `title`,
            // l'autre un 404 nu. Même statut, corps différents : la ressource absente et
            // la ressource d'autrui restaient distinguables, donc l'anti-IDOR d'en
            // dessous ne servait à rien. `PaymentsEndpoints.InitiateAsync` fait déjà
            // ainsi, avec le raisonnement en commentaire.
            return Results.NotFound();
        }
        // Anti-IDOR : seul le propriétaire du paiement (ou un admin) peut le lire.
        if (!principal.IsInRole("Admin") && CurrentUserId(principal) != result.Value.BuyerId)
        {
            return Results.NotFound();
        }
        return Results.Ok(result.Value);
    }

    /// <summary>Récupère le paiement d'une commande (propriétaire ou admin).</summary>
    private static async Task<IResult> GetByOrderAsync(ClaimsPrincipal principal, Guid orderId, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetPaymentByOrderQuery(orderId), ct);
        if (result.IsFailure)
        {
            // Idem. Cette route-ci prend un identifiant de COMMANDE : la distinction
            // révélait l'existence d'une commande, pas seulement d'un paiement.
            return Results.NotFound();
        }
        if (!principal.IsInRole("Admin") && CurrentUserId(principal) != result.Value.BuyerId)
        {
            return Results.NotFound();
        }
        return Results.Ok(result.Value);
    }

    /// <summary>Identifiant du compte connecté (claim « sub »/NameIdentifier).</summary>
    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>Encaisse le paiement (confirmation PSP manuelle) — déclenche la confirmation de commande.</summary>
    private static async Task<IResult> CaptureAsync(Guid id, CaptureRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CapturePaymentCommand(id, request.ProviderReference), ct)).Match(() => Results.NoContent());

    /// <summary>Marque le paiement en échec — déclenche l'annulation de commande.</summary>
    private static async Task<IResult> FailAsync(Guid id, FailRequest? request, ISender sender, CancellationToken ct)
        => (await sender.Send(new FailPaymentCommand(id, request?.Reason ?? string.Empty), ct)).Match(() => Results.NoContent());

    /// <summary>Rembourse un paiement encaissé.</summary>
    private static async Task<IResult> RefundAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new RefundPaymentCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Webhook Stripe : vérifie la signature et applique le résultat au paiement.</summary>
    private static async Task<IResult> StripeWebhookAsync(HttpRequest httpRequest, ISender sender, CancellationToken ct)
        => await HandleWebhookAsync("Stripe", "Stripe-Signature", httpRequest, sender, ct);

    /// <summary>Webhook PayPal : vérifie la signature et applique le résultat au paiement.</summary>
    private static async Task<IResult> PayPalWebhookAsync(HttpRequest httpRequest, ISender sender, CancellationToken ct)
        => await HandleWebhookAsync("PayPal", "Paypal-Transmission-Sig", httpRequest, sender, ct);

    /// <summary>Callback MTN MoMo : statut du RequestToPay (SUCCESSFUL / FAILED).</summary>
    private static async Task<IResult> MomoCallbackAsync(HttpRequest httpRequest, ISender sender, CancellationToken ct)
        => await HandleWebhookAsync("MtnMomo", "X-Callback-Signature", httpRequest, sender, ct);

    /// <summary>Callback Moov Money : statut du RequestToPay (SUCCESSFUL / FAILED).</summary>
    private static async Task<IResult> MoovCallbackAsync(HttpRequest httpRequest, ISender sender, CancellationToken ct)
        => await HandleWebhookAsync("Moov", "X-Callback-Signature", httpRequest, sender, ct);

    /// <summary>
    /// Nombre maximal de caractères lus d'un corps de webhook.
    ///
    /// La signature PSP porte sur les octets reçus : on doit lire avant de juger. Sans
    /// plafond, un appelant anonyme faisait allouer jusqu'aux 30 Mo par défaut de
    /// Kestrel — et ce groupe a `DisableRateLimiting`. Ces charges font quelques
    /// centaines d'octets ; 64 Kio laissent deux ordres de grandeur de marge.
    ///
    /// Le plafond est en CARACTÈRES : c'est l'allocation qu'on borne.
    /// </summary>
    private const int MaxSignedBodyChars = 64 * 1024;

    /// <summary>
    /// Lit le corps brut en refusant ce qui dépasse <see cref="MaxSignedBodyChars"/>.
    /// Renvoie <c>null</c> si le corps est trop grand.
    /// </summary>
    private static async Task<string?> ReadCappedBodyAsync(HttpRequest http, CancellationToken ct)
    {
        http.EnableBuffering();

        // L'en-tête annoncé permet de refuser sans rien lire quand il est là. Il ne fait
        // pas foi — d'où le second contrôle, sur ce qu'on a effectivement lu.
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

    /// <summary>
    /// Webhook FedaPay. FedaPay poste sur la MÊME URL les événements de transaction
    /// (encaissement) et de payout (reversement vendeur) : on aiguille donc d'abord.
    ///
    /// Sans cet aiguillage, un événement de dépôt serait interprété comme un événement de
    /// paiement, et son identifiant — un entier propre aux dépôts, mais collisionnable
    /// avec un identifiant de transaction — irait frapper un PAIEMENT sans rapport.
    /// </summary>
    private static async Task<IResult> FedaPayWebhookAsync(
        HttpRequest httpRequest, ISender sender, IPayoutModuleApi payouts, CancellationToken ct)
    {
        // MÊME PLAFOND QUE LES AUTRES ROUTES SIGNÉES, et ce groupe a en plus le
        // limiteur de débit désactivé — voir MaxSignedBodyChars.
        var rawBody = await ReadCappedBodyAsync(httpRequest, ct);
        if (rawBody is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var signature = httpRequest.Headers["x-fedapay-signature"].ToString();

        var payoutEvent = payouts.ReadPayoutWebhook(rawBody, signature);
        if (!payoutEvent.IsPayoutEvent)
        {
            // Événement de transaction : flux paiement habituel (qui revérifie la signature).
            var payment = await sender.Send(new ProcessGatewayWebhookCommand("FedaPay", rawBody, signature), ct);
            return payment.Match(() => Results.Ok());
        }

        if (!payoutEvent.Verified)
        {
            // Un faux webhook « sent » clôturerait un retrait jamais versé, un faux
            // « failed » déclencherait un remboursement : on rejette sans rien appliquer.
            return Results.Unauthorized();
        }

        var applied = await sender.Send(
            new ApplyPayoutWebhookCommand(payoutEvent.ProviderReference ?? string.Empty, payoutEvent.Progress), ct);

        return applied.Match(() => Results.Ok());
    }

    private static async Task<IResult> HandleWebhookAsync(
        string provider, string signatureHeader, HttpRequest httpRequest, ISender sender, CancellationToken ct)
    {
        var rawBody = await ReadCappedBodyAsync(httpRequest, ct);
        if (rawBody is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var signature = httpRequest.Headers[signatureHeader].ToString();

        var result = await sender.Send(new ProcessGatewayWebhookCommand(provider, rawBody, signature), ct);
        return result.Match(() => Results.Ok());
    }

    /// <summary>Retour de redirection : confirme le paiement en interrogeant le PSP.</summary>
    private static async Task<IResult> ReturnAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ConfirmPaymentFromRedirectCommand(id), ct)).Match(() => Results.Ok(new { paymentId = id, status = "confirmed" }));

    public sealed record InitiateRequest(Guid OrderId, string Method, string Provider, string? Flow, string? ReturnUrl, string? CancelUrl, string PayerPhone);
    public sealed record CaptureRequest(string ProviderReference);
    public sealed record FailRequest(string? Reason);
}
