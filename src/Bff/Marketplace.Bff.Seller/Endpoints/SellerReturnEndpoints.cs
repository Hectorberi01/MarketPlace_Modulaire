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
        // ═════════════════════════════════════════════════════════════════════════
        // L'ARGENT PART ICI. CE COMMENTAIRE DISAIT LE CONTRAIRE, ET L'ÉCRAN LE RECOPIAIT.
        //
        // Il affirmait : « l'argent ne part PAS ici [...] le versement est exécuté par un
        // administrateur dans le tableau de bord FedaPay ». C'était vrai avant que
        // Settlement ne s'abonne à l'événement. Ce n'est plus le cas, et la console
        // vendeur promettait encore, mot pour mot, qu'aucun versement n'était déclenché.
        //
        // La chaîne réelle, depuis `ReturnRequest.ApproveRefund` :
        //   ReturnRefundApprovedDomainEvent
        //     → CreditCustomerWalletOnReturnRefundApprovedHandler  crédite la CAGNOTTE
        //                                                          de l'acheteur
        //     → ConfirmRefundOnCustomerWalletCreditedHandler       statut « Refunded »
        //     → ReverseEarningsOnReturnRefundedHandler:123         DÉBITE LE VENDEUR
        //                                                          de sa part nette
        //
        // Aucun administrateur ne s'intercale, et « attente de versement » ne dure que
        // le temps de trois sauts d'outbox. La cagnotte n'est pas un virement bancaire —
        // c'est un avoir chez nous — mais c'est bien de l'argent qui change de mains.
        // ═════════════════════════════════════════════════════════════════════════
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
    /// À NOTER POUR LE REMBOURSEMENT, ET CE PARAGRAPHE DISAIT L'INVERSE : la validation
    /// FAIT partir l'argent. `ApproveRefund` lève `ReturnRefundApprovedDomainEvent`, que
    /// Settlement consomme pour créditer la cagnotte de l'acheteur puis contre-passer la
    /// part nette du vendeur ; aucun administrateur ne s'intercale (voir RefundAsync,
    /// dans ce même fichier, qui décrit la chaîne complète).
    ///
    /// La conclusion qu'on en tirait — « autoriser la validation ne fait sortir aucun
    /// franc d'une boutique sous mesure conservatoire » — était donc une garantie de
    /// sécurité fondée sur un circuit disparu. L'ouverture reste la bonne décision, mais
    /// pour l'autre raison, celle du paragraphe ci-dessus : le retour est un devoir
    /// envers un acheteur qui a payé, et le levier contre une boutique suspendue est le
    /// blocage du RETRAIT (SellerRights.CanWithdraw), pas celui du remboursement.
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
