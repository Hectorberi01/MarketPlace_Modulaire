using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Returns.Application.Returns;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — retours (RMA). Liste scopée à la boutique du jeton ; chaque
/// transition par id vérifie l'appartenance (anti-IDOR). Cycle : approuver /
/// refuser → suivi → reçu → remboursé.
/// </summary>
public static class SellerReturnEndpoints
{
    public static IEndpointRouteBuilder MapSellerReturnEndpoints(this IEndpointRouteBuilder app)
    {
        var returns = app.MapSellerGroup("/seller/returns", "Vendeur · Retours");

        returns.MapGet("/", ListMineAsync);
        returns.MapGet("/{id:guid}", GetAsync);
        returns.MapPost("/{id:guid}/approve", ApproveAsync);
        returns.MapPost("/{id:guid}/reject", RejectAsync);
        returns.MapPost("/{id:guid}/tracking", TrackingAsync);
        returns.MapPost("/{id:guid}/received", ReceivedAsync);
        returns.MapPost("/{id:guid}/refund", RefundAsync);

        return app;
    }

    /// <summary>Retours de la boutique du jeton.</summary>
    private static async Task<IResult> ListMineAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new ListSellerReturnsQuery(seller.Id), ct)).Match(r => Results.Ok(r));
    }

    /// <summary>Détail d'un retour (scopé : 404 s'il n'appartient pas au vendeur).</summary>
    private static async Task<IResult> GetAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        return await SellerBff.OwnsReturnAsync(sender, seller.Id, id, ct)
            ? (await sender.Send(new GetReturnQuery(id), ct)).Match(r => Results.Ok(r))
            : SellerBff.NotFoundOrForbidden("return");
    }

    private static async Task<IResult> ApproveAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct, () => sender.Send(new ApproveReturnCommand(id), ct));

    private static async Task<IResult> RejectAsync(ClaimsPrincipal user, Guid id, RejectRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct, () => sender.Send(new RejectReturnCommand(id, request.Reason), ct));

    private static async Task<IResult> TrackingAsync(ClaimsPrincipal user, Guid id, TrackingRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct, () => sender.Send(new AddReturnTrackingCommand(id, request.Carrier, request.TrackingNumber), ct));

    private static async Task<IResult> ReceivedAsync(ClaimsPrincipal user, Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        => await GuardAsync(user, sender, sellers, id, ct, () => sender.Send(new MarkReturnReceivedCommand(id), ct));

    private static async Task<IResult> RefundAsync(ClaimsPrincipal user, Guid id, RefundRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
        // VALIDE le remboursement — l'argent ne part PAS ici.
        //
        // Le vendeur décide et fixe le montant ; le versement, lui, est exécuté par un
        // administrateur dans le tableau de bord FedaPay (aucune API de remboursement
        // n'existe). Le retour passe donc en « attente de versement », et non en
        // « remboursé ».
        //
        // C'est précisément la confusion d'hier : ce bouton marquait « remboursé » sans
        // qu'un seul franc ne quitte le compte.
        => await GuardAsync(user, sender, sellers, id, ct, () => sender.Send(new ApproveReturnRefundCommand(id, request.Amount), ct));

    /// <summary>
    /// Résout le vendeur, vérifie la propriété du retour, puis exécute la commande.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// PAS DE CONTRÔLE DE STATUT, POUR LA MÊME RAISON QUE LES EXPÉDITIONS.
    ///
    /// Approuver, refuser, suivre, réceptionner, valider un remboursement : chacune de
    /// ces transitions est un DEVOIR envers un acheteur qui a déjà payé, pas un droit
    /// commercial que la plateforme accorde. Une boutique suspendue qui ne pourrait
    /// plus traiter ses retours laisserait ses clients sans recours et ses RMA figés
    /// en attente — la sanction retomberait sur eux.
    ///
    /// À noter pour le remboursement : le vendeur VALIDE ici, l'argent part par la
    /// main d'un administrateur (voir RefundAsync). Autoriser la validation ne fait
    /// donc sortir aucun franc d'une boutique sous mesure conservatoire.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    private static async Task<IResult> GuardAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, Guid returnId, CancellationToken ct,
        Func<Task<Marketplace.BuildingBlocks.Domain.Results.Result>> action)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }
        if (!await SellerBff.OwnsReturnAsync(sender, seller.Id, returnId, ct))
        {
            return SellerBff.NotFoundOrForbidden("return");
        }

        return (await action()).Match(() => Results.NoContent());
    }

    public sealed record RejectRequest(string Reason);
    public sealed record TrackingRequest(string Carrier, string TrackingNumber);
    public sealed record RefundRequest(decimal Amount);
}
