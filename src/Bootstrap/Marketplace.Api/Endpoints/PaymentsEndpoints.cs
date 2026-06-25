using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Payments.Application.Payments.Commands;
using Marketplace.Modules.Payments.Application.Payments.Commands.InitiatePayment;
using Marketplace.Modules.Payments.Application.Payments.Queries;

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
        var payments = app.MapGroup("/api/payments").WithTags("Payments").RequireAuthorization();

        payments.MapPost("/", InitiateAsync).WithName("InitiatePayment");
        payments.MapGet("/{id:guid}", GetAsync).WithName("GetPayment");
        payments.MapGet("/order/{orderId:guid}", GetByOrderAsync).WithName("GetPaymentByOrder");
        payments.MapPost("/{id:guid}/capture", CaptureAsync).WithName("CapturePayment");

        // Échec / remboursement : actions sensibles, réservées Admin (simulent le PSP).
        payments.MapPost("/{id:guid}/fail", FailAsync).WithName("FailPayment").RequireAuthorization(p => p.RequireRole("Admin"));
        payments.MapPost("/{id:guid}/refund", RefundAsync).WithName("RefundPayment").RequireAuthorization(p => p.RequireRole("Admin"));

        // Webhooks PSP + retour de redirection : appelés par le PSP / le navigateur, donc anonymes.
        var gateway = app.MapGroup("/api/payments").WithTags("Payments");
        gateway.MapPost("/webhooks/stripe", StripeWebhookAsync).WithName("StripeWebhook").AllowAnonymous();
        gateway.MapPost("/webhooks/paypal", PayPalWebhookAsync).WithName("PayPalWebhook").AllowAnonymous();
        gateway.MapPost("/webhooks/momo", MomoCallbackAsync).WithName("MomoCallback").AllowAnonymous();
        gateway.MapPost("/webhooks/moov", MoovCallbackAsync).WithName("MoovCallback").AllowAnonymous();
        gateway.MapGet("/return/{id:guid}", ReturnAsync).WithName("ConfirmPaymentReturn").AllowAnonymous();

        return app;
    }

    /// <summary>Initie un paiement et renvoie l'URL de redirection (checkout) ou le client secret (intent).</summary>
    private static async Task<IResult> InitiateAsync(InitiateRequest request, ISender sender, CancellationToken ct)
    {
        var command = new InitiatePaymentCommand(
            request.OrderId, request.Method, request.Provider,
            request.Flow ?? "HostedCheckout", request.ReturnUrl, request.CancelUrl, request.PayerPhone);
        var result = await sender.Send(command, ct);
        return result.Match(r => Results.Ok(r));
    }

    /// <summary>Récupère un paiement par son identifiant.</summary>
    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetPaymentQuery(id), ct)).Match(payment => Results.Ok(payment));

    /// <summary>Récupère le paiement d'une commande.</summary>
    private static async Task<IResult> GetByOrderAsync(Guid orderId, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetPaymentByOrderQuery(orderId), ct)).Match(payment => Results.Ok(payment));

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

    private static async Task<IResult> HandleWebhookAsync(
        string provider, string signatureHeader, HttpRequest httpRequest, ISender sender, CancellationToken ct)
    {
        using var reader = new StreamReader(httpRequest.Body);
        var rawBody = await reader.ReadToEndAsync(ct);
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
