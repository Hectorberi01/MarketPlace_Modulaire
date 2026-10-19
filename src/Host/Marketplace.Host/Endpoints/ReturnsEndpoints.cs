using System.Security.Claims;
using MediatR;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Returns.Application.Returns;

namespace Marketplace.Host.Endpoints;

/// <summary>
/// Endpoints HTTP du module Returns (RMA) : demande de retour par l'acheteur,
/// traitement par le vendeur (approbation, réception, remboursement).
/// </summary>
public static class ReturnsEndpoints
{
    /// <summary>Enregistre les routes de retour.</summary>
    public static IEndpointRouteBuilder MapReturnsEndpoints(this IEndpointRouteBuilder app)
    {
        // ═════════════════════════════════════════════════════════════════════════
        // DEUX GROUPES, PARCE QUE CES ROUTES N'ONT PAS LE MÊME PUBLIC.
        //
        // Il n'y en avait qu'un — `MapGroup("/api/returns").RequireAuthorization()` —
        // et le cycle de vie du retour était donc ouvert à TOUT COMPTE AUTHENTIFIÉ,
        // sans le moindre contrôle de propriété, ni à l'endpoint ni dans le handler
        // (ReturnMutationHandlerBase.MutateAsync charge par identifiant et mute).
        //
        // CE QUE CELA PERMETTAIT, CONCRÈTEMENT. `POST /{id}/refund` lève
        // ReturnRefundApprovedDomainEvent, que Settlement transforme en CRÉDIT RÉEL de
        // la cagnotte client. Un acheteur ouvrait un retour légitime sur sa propre
        // commande — cette route-là vérifie bien la propriété — puis enchaînait
        // approve → received → refund sur son propre identifiant : remboursé, et il
        // garde la marchandise. Sur l'identifiant d'un tiers, il remboursait un inconnu
        // aux frais de la plateforme. Le plafond RefundableAmount borne le MONTANT ;
        // rien ne bornait QUI décide.
        //
        // C'est le même motif que POST /api/shipments/{id}/deliver, corrigé de la même
        // façon : le cycle de vie passe au back-office.
        //
        // LE CHEMIN VENDEUR N'EST PAS TOUCHÉ. Il existe déjà, et il est correct :
        // /seller/returns (BFF Vendeur) exige le rôle Seller ET vérifie la propriété du
        // retour à chaque transition (SellerBff.OwnsReturnAsync). C'est là que passent
        // les vendeurs ; ces routes-ci sont le back-office.
        // ═════════════════════════════════════════════════════════════════════════
        var mine = app.MapAuthenticatedGroup("/api/returns").WithTags("Returns");

        mine.MapPost("/", RequestAsync).WithName("RequestReturn");
        mine.MapGet("/mine", ListMineAsync).WithName("ListMyReturns");

        // Lecture unitaire : ACHETEUR PROPRIÉTAIRE ou admin — le contrôle est dans
        // GetAsync, calqué sur celui d'OrderingEndpoints.
        mine.MapGet("/{id:guid}", GetAsync).WithName("GetReturn");

        // ═════════════════════════════════════════════════════════════════════════
        // ADMIN SEUL, ET LE `MapAdminGroup` NE SUFFISAIT PAS À LE DIRE.
        //
        // `MapAdminGroup` exige `RequireRole(Admin, Moderator)` — son nom laisse croire
        // à un groupe administrateur, il ouvre en réalité au back-office entier.
        //
        // Or `/{id}/refund` déclenche `ApproveReturnRefundCommand`, qui publie
        // `ReturnRefundApprovedIntegrationEvent`, que `CreditCustomerWalletOn...` consomme
        // en CRÉDITANT LA CAGNOTTE D'UN CLIENT aux frais de la plateforme. Un modérateur
        // enchaînait `approve → received → refund` sur n'importe quel retour.
        //
        // C'est le raisonnement qui avait retiré `/admin/outbox` aux modérateurs, et la
        // parade était déjà écrite deux fichiers plus loin : `SettlementEndpoints` et
        // `BillingEndpoints` rechaînent ce `RequireRole("Admin")`. Celui-ci l'oubliait.
        //
        // Le durcissement porte sur TOUT le groupe, pas sur la seule route qui paie :
        // `approve` et `received` sont les deux marches qui y mènent, et les isoler
        // reviendrait à garder la porte en laissant l'escalier.
        // ═════════════════════════════════════════════════════════════════════════
        var backOffice = app.MapAdminGroup("/api/returns").WithTags("Returns")
            .RequireAuthorization(p => p.RequireRole("Admin"));

        backOffice.MapGet("/seller/{sellerId:guid}", ListSellerAsync).WithName("ListSellerReturns");
        backOffice.MapPost("/{id:guid}/approve", ApproveAsync).WithName("ApproveReturn");
        backOffice.MapPost("/{id:guid}/reject", RejectAsync).WithName("RejectReturn");
        backOffice.MapPost("/{id:guid}/tracking", TrackingAsync).WithName("AddReturnTracking");
        backOffice.MapPost("/{id:guid}/received", ReceivedAsync).WithName("MarkReturnReceived");
        backOffice.MapPost("/{id:guid}/refund", RefundAsync).WithName("RefundReturn");

        return app;
    }

    /// <summary>Ouvre une demande de retour pour une ligne de commande de l'acheteur connecté.</summary>
    private static async Task<IResult> RequestAsync(RequestBody body, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new RequestReturnCommand(buyerId, body.OrderId, body.OfferId, body.Reason), ct);
        return result.Match(id => Results.Created($"/api/returns/{id}", new { id }));
    }

    /// <summary>Liste les retours de l'acheteur connecté.</summary>
    private static async Task<IResult> ListMineAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ListMyReturnsQuery(buyerId), ct);
        return result.Match(items => Results.Ok(items));
    }

    /// <summary>Liste les retours d'un vendeur.</summary>
    private static async Task<IResult> ListSellerAsync(Guid sellerId, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListSellerReturnsQuery(sellerId), ct);
        return result.Match(items => Results.Ok(items));
    }

    /// <summary>Récupère une demande de retour.</summary>
    private static async Task<IResult> GetAsync(
        ClaimsPrincipal principal, Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetReturnQuery(id), ct);
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

        // ANTI-IDOR. Sans ce contrôle, n'importe quel compte lisait le retour de
        // n'importe qui : identifiants d'acheteur et de vendeur, montant remboursable,
        // transporteur et numéro de suivi. Le 404 plutôt qu'un 403 : confirmer
        // l'existence d'un retour qu'on n'a pas le droit de voir est déjà une fuite.
        if (!principal.IsInRole("Admin") && CurrentUserId(principal) != result.Value.BuyerId)
        {
            return Results.NotFound();
        }

        return Results.Ok(result.Value);
    }

    /// <summary>Approuve la demande de retour.</summary>
    private static async Task<IResult> ApproveAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ApproveReturnCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Rejette la demande de retour.</summary>
    private static async Task<IResult> RejectAsync(Guid id, ReasonBody? body, ISender sender, CancellationToken ct)
        => (await sender.Send(new RejectReturnCommand(id, body?.Reason ?? string.Empty), ct)).Match(() => Results.NoContent());

    /// <summary>Renseigne le colis retour.</summary>
    private static async Task<IResult> TrackingAsync(Guid id, TrackingBody body, ISender sender, CancellationToken ct)
        => (await sender.Send(new AddReturnTrackingCommand(id, body.Carrier, body.TrackingNumber), ct)).Match(() => Results.NoContent());

    /// <summary>Marque le colis retour comme réceptionné.</summary>
    private static async Task<IResult> ReceivedAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new MarkReturnReceivedCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Déclenche le remboursement du retour.</summary>
    private static async Task<IResult> RefundAsync(Guid id, RefundBody body, ISender sender, CancellationToken ct)
        => (await sender.Send(new ApproveReturnRefundCommand(id, body.Amount), ct)).Match(() => Results.NoContent());

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    public sealed record RequestBody(Guid OrderId, Guid OfferId, string Reason);
    public sealed record ReasonBody(string? Reason);
    public sealed record TrackingBody(string Carrier, string TrackingNumber);
    public sealed record RefundBody(decimal Amount);
}
