using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Disputes.Application.Disputes;
using Marketplace.Modules.Disputes.Contracts;
using Marketplace.Modules.Shipping.Application.Shipments.Queries;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — litiges. Le module ne lie pas les litiges à un vendeur ; on
/// utilise donc comme preuve d'implication le fait que le vendeur ait une
/// expédition sur la commande concernée (anti-fuite entre vendeurs).
/// </summary>
public static class SellerDisputeEndpoints
{
    public static IEndpointRouteBuilder MapSellerDisputeEndpoints(this IEndpointRouteBuilder app)
    {
        var disputes = app.MapSellerGroup("/seller/disputes", "Vendeur · Litiges");

        disputes.MapGet("/", ListMineAsync);
        disputes.MapGet("/by-order/{orderId:guid}", ByOrderAsync);
        disputes.MapGet("/{id:guid}", GetAsync);
        disputes.MapPost("/{id:guid}/messages", AddMessageAsync);

        return app;
    }

    /// <summary>
    /// Litiges où ce vendeur est impliqué.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// POURQUOI CETTE ROUTE N'EXISTAIT PAS, ET POURQUOI ELLE MANQUE
    ///
    /// Le module Litiges ne connaît ni vendeur ni boutique : un litige est rattaché à
    /// une COMMANDE et à l'acheteur qui l'a ouvert. On ne pouvait donc consulter un
    /// litige qu'en partant d'une commande précise — ce qui suppose de savoir laquelle
    /// chercher. Un vendeur n'avait aucun moyen de découvrir qu'un litige le concernait :
    /// il fallait le lui dire par un autre canal.
    ///
    /// DEUX REQUÊTES, PAS N+1
    ///
    /// On récupère d'abord les expéditions du vendeur (une requête), on en tire les
    /// commandes qui le concernent, puis on demande les litiges DE CES COMMANDES (une
    /// seconde). L'alternative naturelle — vérifier l'implication litige par litige —
    /// appelait le module Expéditions autant de fois qu'il y a de litiges.
    ///
    /// Le filtrage est fait PAR LE MODULE, en base. La première version passait par
    /// `ListAllDisputesQuery` puis filtrait en mémoire : le plafond de 500 s'appliquait
    /// alors AVANT le filtre, donc aux litiges de toute la plateforme. Passé ce volume,
    /// un litige ouvert d'un vendeur pouvait sortir de la fenêtre et disparaître de son
    /// écran — remplacé par un « aucun litige » parfaitement rassurant.
    ///
    /// MÊME CRITÈRE D'IMPLICATION QUE `IsInvolvedInOrderAsync` : « le vendeur a une
    /// expédition sur cette commande ». Ce n'est pas un détail. Lister selon les LIGNES
    /// de commande aurait été plus large, et aurait affiché des litiges dont le détail
    /// répond 403 — un écran qui montre des lignes sur lesquelles on ne peut pas
    /// cliquer, sans un mot d'explication.
    ///
    /// Conséquence assumée : un litige ouvert avant la moindre expédition n'apparaît
    /// pas. C'est la définition d'« impliqué » retenue par le contrôle d'accès existant ;
    /// la changer est un autre chantier, qui doit alors changer les deux ensemble.
    ///
    /// La liste source est bornée (les 500 litiges les plus récents de la plateforme) :
    /// c'est une file de travail, pas un historique exhaustif.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static async Task<IResult> ListMineAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var shipments = await sender.Send(new ListShipmentsBySellerQuery(seller.Id), ct);
        if (shipments.IsFailure)
        {
            return shipments.Match(s => Results.Ok(s));
        }

        var myOrders = shipments.Value.Select(s => s.OrderId).Distinct().ToList();
        if (myOrders.Count == 0)
        {
            return Results.Ok(Array.Empty<DisputeSummary>());
        }

        return (await sender.Send(new ListDisputesByOrdersQuery(myOrders), ct)).Match(d => Results.Ok(d));
    }

    /// <summary>Litiges d'une commande où le vendeur est impliqué.</summary>
    private static async Task<IResult> ByOrderAsync(ClaimsPrincipal user, Guid orderId, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }
        if (!await SellerBff.IsInvolvedInOrderAsync(sender, seller.Id, orderId, ct))
        {
            return SellerBff.NotFoundOrForbidden("order");
        }

        return (await sender.Send(new ListDisputesByOrderQuery(orderId), ct)).Match(d => Results.Ok(d));
    }

    /// <summary>Détail d'un litige (si le vendeur est impliqué dans sa commande).</summary>
    private static async Task<IResult> GetAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var dispute = await sender.Send(new GetDisputeQuery(id), ct);
        if (dispute.IsFailure)
        {
            // MÊME RÉPONSE QUE « PAS CONCERNÉ », en dessous. Celle-ci rendait un
            // ProblemDetails `disputes.not_found`, l'autre `{ error: "dispute_not_found" }` :
            // deux corps distincts sous un même 404, donc n'importe quel compte à rôle
            // Seller énumérait les litiges de la plateforme.
            //
            // Les fichiers voisins (offres, retours, expéditions, stock) contrôlent la
            // propriété AVANT de charger, ce qui fait tomber les deux cas dans une seule
            // réponse. Ici la propriété se déduit de la commande du litige, donc il faut
            // le charger d'abord — d'où l'alignement explicite.
            return SellerBff.NotFoundOrForbidden("dispute");
        }
        if (!await SellerBff.IsInvolvedInOrderAsync(sender, seller.Id, dispute.Value.OrderId, ct))
        {
            return SellerBff.NotFoundOrForbidden("dispute");
        }

        return Results.Ok(dispute.Value);
    }

    /// <summary>
    /// Ajoute un message au litige (auteur = utilisateur du jeton).
    ///
    /// PAS DE CONTRÔLE DE STATUT, DÉLIBÉRÉMENT. C'est une écriture, mais son contenu
    /// n'est ni de la vitrine ni de l'offre : c'est la DÉFENSE du vendeur dans une
    /// procédure ouverte contre lui. Un litige est d'ailleurs souvent le motif même de
    /// la suspension — le faire taire à cet instant reviendrait à instruire à charge,
    /// et priverait la modération de la seule version des faits qu'elle n'a pas.
    /// </summary>
    private static async Task<IResult> AddMessageAsync(ClaimsPrincipal user, Guid id, MessageRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        if (SellerBff.CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var dispute = await sender.Send(new GetDisputeQuery(id), ct);
        if (dispute.IsFailure)
        {
            // Idem que sur la lecture ci-dessus.
            return SellerBff.NotFoundOrForbidden("dispute");
        }
        if (!await SellerBff.IsInvolvedInOrderAsync(sender, seller.Id, dispute.Value.OrderId, ct))
        {
            return SellerBff.NotFoundOrForbidden("dispute");
        }

        return (await sender.Send(new AddDisputeMessageCommand(id, userId, request.Body, request.PhotoUrl), ct))
            .Match(() => Results.NoContent());
    }

    public sealed record MessageRequest(string Body, string? PhotoUrl);
}
