using System.Security.Claims;
using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Ordering.Application.Orders.Commands;
using Marketplace.Modules.Ordering.Application.Orders.Queries;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Ordering : SUIVI et TRANSITIONS d'une commande
/// (lecture, annulation, confirmation de paiement, livraison).
///
/// LE PLACEMENT N'EST PAS ICI. Il vit dans le BFF Client (`POST /client/checkout/pay`),
/// seule surface qui dispose de l'adresse retenue, du tarif de la zone et du total
/// affiché à l'acheteur. Cette documentation affirmait le contraire alors que la route
/// de placement répondait 400 depuis des semaines — voir le commentaire sur le groupe
/// de routes.
/// </summary>
public static class OrderingEndpoints
{
    /// <summary>Enregistre les routes de commande (acheteur authentifié).</summary>
    public static IEndpointRouteBuilder MapOrderingEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapAuthenticatedGroup("/api/orders").WithTags("Ordering");

        // ═════════════════════════════════════════════════════════════════════════
        // LE PLACEMENT DE COMMANDE N'EST PLUS ICI. `POST /api/orders` A ÉTÉ SUPPRIMÉ.
        //
        // La route existait encore et répondait 400 EN PERMANENCE : elle envoyait
        // `new PlaceOrderCommand(buyerId)` sans adresse, et depuis que l'adresse de
        // livraison est un invariant du handler, elle ne pouvait plus rien créer. Sa
        // documentation continuait pourtant d'affirmer qu'elle fonctionnait.
        //
        // POURQUOI SUPPRIMER PLUTÔT QUE RÉPARER.
        //
        // Placer une commande demande aujourd'hui trois choses que seule une surface
        // d'achat possède : l'adresse retenue, le tarif de la zone de destination, et
        // le total que l'acheteur A VU. Rétablir tout cela ici donnerait DEUX
        // valorisations concurrentes du même panier — précisément le défaut que le
        // contrôle `expectedTotal` a été posé pour rattraper, reproduit une couche
        // plus bas et sans personne pour le voir.
        //
        // Le placement vit donc en un seul endroit : `POST /client/checkout/pay`, dans
        // le BFF Client. Les routes ci-dessous restent : elles LISENT une commande ou
        // la font TRANSITER, ce qui ne demande aucun prix.
        // ═════════════════════════════════════════════════════════════════════════
        orders.MapGet("/", ListMineAsync).WithName("ListMyOrders");
        orders.MapGet("/{id:guid}", GetAsync).WithName("GetOrder");
        orders.MapPost("/{id:guid}/cancel", CancelAsync).WithName("CancelOrder");

        // Confirmation de paiement : simule le webhook PSP — réservé Admin tant que Payments n'existe pas.
        orders.MapPost("/{id:guid}/confirm-payment", ConfirmPaymentAsync)
            .WithName("ConfirmOrderPayment").RequireAuthorization(p => p.RequireRole("Admin"));

        // Confirmation de livraison (logistique) : déclenche escrow + payout vendeur.
        orders.MapPost("/{id:guid}/deliver", DeliverAsync)
            .WithName("MarkOrderDelivered").RequireAuthorization(p => p.RequireRole("Admin"));

        return app;
    }

    /// <summary>Liste les commandes de l'acheteur connecté.</summary>
    private static async Task<IResult> ListMineAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ListMyOrdersQuery(buyerId), ct);
        return result.Match(orders => Results.Ok(orders));
    }

    /// <summary>Récupère une commande par son identifiant (propriétaire ou admin).</summary>
    private static async Task<IResult> GetAsync(ClaimsPrincipal principal, Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetOrderQuery(id), ct);
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
        // Anti-IDOR : seul le propriétaire (ou un admin) peut lire la commande.
        if (!principal.IsInRole("Admin") && CurrentUserId(principal) != result.Value.BuyerId)
        {
            return Results.NotFound();
        }
        return Results.Ok(result.Value);
    }

    /// <summary>Annule une commande et libère ses réservations (propriétaire ou admin).</summary>
    private static async Task<IResult> CancelAsync(ClaimsPrincipal principal, Guid id, CancelRequest? request, ISender sender, CancellationToken ct)
    {
        var orderResult = await sender.Send(new GetOrderQuery(id), ct);
        if (orderResult.IsFailure)
        {
            // Même correction que sur la lecture ci-dessus — et elle compte davantage
            // ici : c'est une MUTATION, donc un identifiant valide se confirme sans même
            // avoir à lire la commande.
            return Results.NotFound();
        }
        if (!principal.IsInRole("Admin") && CurrentUserId(principal) != orderResult.Value.BuyerId)
        {
            return Results.NotFound();
        }
        var result = await sender.Send(new CancelOrderCommand(id, request?.Reason ?? string.Empty), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Confirme le paiement : solde le stock et confirme la commande.</summary>
    private static async Task<IResult> ConfirmPaymentAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ConfirmOrderPaymentCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Marque la commande livrée : libère l'escrow et déclenche le payout vendeur.</summary>
    private static async Task<IResult> DeliverAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new MarkOrderDeliveredCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    public sealed record CancelRequest(string? Reason);
}
