using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;
using MediatR;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Payments.Application.Payments.Commands;
using Marketplace.Modules.Payments.Application.Payments.Commands.InitiatePayment;
using Marketplace.Modules.Payments.Application.Payments.Queries;
using Marketplace.Modules.Ordering.Application.Orders.Queries;
using Marketplace.Modules.Payments.Contracts;
using Marketplace.Modules.Settlement.Application.Wallets;

namespace Marketplace.Host.Endpoints;

/// <summary>
/// Endpoints HTTP du module Payments. Initiation via PSP (Stripe / PayPal) en checkout
/// hébergé ou intention serveur ; confirmation par webhook signé ou au retour de
/// redirection. La capture confirme la commande, l'échec l'annule, via events.
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

        // Webhooks PSP + retour de redirection : appelés par le PSP ou le navigateur,
        // donc anonymes.
        //
        // `DisableRateLimiting()` est indispensable : les webhooks d'un PSP arrivent tous
        // des mêmes adresses, donc d'une seule partition. Un pic de commandes dépasserait
        // le quota et FedaPay recevrait des 429 — des paiements encaissés chez le PSP et
        // jamais confirmés chez nous. La défense ici est la SIGNATURE (HMAC, comparaison
        // en temps constant, fail-closed en production) ; le débit brut reste borné par
        // Traefik.
        var gateway = app.MapGroup("/api/payments").WithTags("Payments").DisableRateLimiting();
        gateway.MapPost("/webhooks/stripe", StripeWebhookAsync).WithName("StripeWebhook").AllowAnonymous();
        gateway.MapPost("/webhooks/paypal", PayPalWebhookAsync).WithName("PayPalWebhook").AllowAnonymous();
        gateway.MapPost("/webhooks/momo", MomoCallbackAsync).WithName("MomoCallback").AllowAnonymous();
        gateway.MapPost("/webhooks/moov", MoovCallbackAsync).WithName("MoovCallback").AllowAnonymous();
        gateway.MapPost("/webhooks/fedapay", FedaPayWebhookAsync).WithName("FedaPayWebhook").AllowAnonymous();
        gateway.MapPost("/webhooks/kkiapay", KkiapayWebhookAsync).WithName("KkiapayWebhook").AllowAnonymous();
        gateway.MapGet("/return/{id:guid}", ReturnAsync).WithName("ConfirmPaymentReturn").AllowAnonymous();

        return app;
    }

    /// <summary>Initie un paiement et renvoie l'URL de redirection (checkout) ou le client secret (intent).</summary>
    private static async Task<IResult> InitiateAsync(
        ClaimsPrincipal principal, InitiateRequest request, ISender sender, CancellationToken ct)
    {
        // L'identifiant de commande vient du CORPS : il faut donc vérifier qu'elle
        // appartient à l'appelant. Sans ce contrôle, un paiement « Pending » posé sur la
        // commande d'un tiers la rend impayable (« payments.already_exists ») ; les
        // réponses distinguent « introuvable », « non payable » et « déjà existant », donc
        // les commandes sont énumérables ; et l'URL de retour PSP devient celle de
        // l'attaquant.
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
            // Les deux branches répondent la même chose : même statut ET même corps. Un
            // ProblemDetails d'un côté et un 404 nu de l'autre rendraient la ressource
            // absente distinguable de celle d'autrui, et l'anti-IDOR en dessous inutile.
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
    /// Rappel KkiaPay. L'en-tête x-kkiapay-secret porte le secret du tableau de bord
    /// LUI-MÊME, pas une signature du corps : la passerelle le compare en temps
    /// constant au secret configuré.
    /// </summary>
    private static async Task<IResult> KkiapayWebhookAsync(HttpRequest httpRequest, ISender sender, CancellationToken ct)
        => await HandleWebhookAsync("Kkiapay", "x-kkiapay-secret", httpRequest, sender, ct);

    /// <summary>
    /// Nombre maximal de caractères lus d'un corps de webhook. La signature porte sur les
    /// octets reçus : on lit avant de juger. Sans plafond, un appelant anonyme fait
    /// allouer les 30 Mo par défaut de Kestrel, sur un groupe qui a `DisableRateLimiting`.
    /// Plafond en CARACTÈRES : c'est l'allocation qu'on borne.
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
    /// Webhook FedaPay. FedaPay poste les transactions (encaissement) et les payouts
    /// (reversement vendeur) sur la MÊME URL : on aiguille d'abord. Sans cela un
    /// événement de dépôt frapperait un paiement sans rapport — les identifiants de dépôt
    /// et de transaction peuvent collisionner.
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
