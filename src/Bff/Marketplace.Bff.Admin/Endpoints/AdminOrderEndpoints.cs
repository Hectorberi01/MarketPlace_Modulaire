using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Ordering.Application.Orders.Commands;
using Marketplace.Modules.Ordering.Application.Orders.Queries;
using Marketplace.Modules.Payments.Application.Payments.Commands;
using Marketplace.Modules.Payments.Application.Payments.Queries;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>
/// BFF Admin — opérations commandes &amp; paiements : intervention manuelle sur le
/// cycle de vie (confirmer un paiement, annuler, marquer livré) et gestion des
/// paiements (échec, remboursement).
/// </summary>
public static class AdminOrderEndpoints
{
    public static IEndpointRouteBuilder MapAdminOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapAdminGroup("/admin/orders", "Admin · Commandes");
        orders.MapGet("/{id:guid}", GetOrderAsync);
        orders.MapPost("/{id:guid}/confirm-payment", ConfirmPaymentAsync);
        orders.MapPost("/{id:guid}/cancel", CancelOrderAsync);
        orders.MapPost("/{id:guid}/deliver", DeliverAsync);

        var payments = app.MapAdminGroup("/admin/payments", "Admin · Paiements");
        payments.MapGet("/{id:guid}", GetPaymentAsync);
        payments.MapGet("/by-order/{orderId:guid}", GetPaymentByOrderAsync);
        payments.MapPost("/{id:guid}/fail", FailPaymentAsync);
        payments.MapPost("/{id:guid}/refund", RefundPaymentAsync);

        return app;
    }

    /// <summary>Détail d'une commande.</summary>
    private static async Task<IResult> GetOrderAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetOrderQuery(id), ct)).Match(o => Results.Ok(o));

    /// <summary>Confirme le paiement (solde le stock + confirme la commande).</summary>
    private static async Task<IResult> ConfirmPaymentAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ConfirmOrderPaymentCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Annule la commande (libère les réservations).</summary>
    private static async Task<IResult> CancelOrderAsync(Guid id, CancelRequest? request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CancelOrderCommand(id, request?.Reason ?? "Annulée par l'administration."), ct)).Match(() => Results.NoContent());

    /// <summary>Marque la commande livrée (déclenche escrow + payout en aval).</summary>
    private static async Task<IResult> DeliverAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new MarkOrderDeliveredCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Détail d'un paiement.</summary>
    private static async Task<IResult> GetPaymentAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetPaymentQuery(id), ct)).Match(p => Results.Ok(p));

    /// <summary>Paiement d'une commande.</summary>
    private static async Task<IResult> GetPaymentByOrderAsync(Guid orderId, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetPaymentByOrderQuery(orderId), ct)).Match(p => Results.Ok(p));

    /// <summary>Marque un paiement en échec.</summary>
    private static async Task<IResult> FailPaymentAsync(Guid id, FailRequest? request, ISender sender, CancellationToken ct)
        => (await sender.Send(new FailPaymentCommand(id, request?.Reason ?? "Échec constaté par l'administration."), ct)).Match(() => Results.NoContent());

    /// <summary>Rembourse un paiement.</summary>
    private static async Task<IResult> RefundPaymentAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new RefundPaymentCommand(id), ct)).Match(() => Results.NoContent());

    public sealed record CancelRequest(string? Reason);
    public sealed record FailRequest(string? Reason);
}
