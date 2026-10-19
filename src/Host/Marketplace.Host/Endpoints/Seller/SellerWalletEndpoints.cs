using MediatR;
using System.Security.Claims;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Settlement.Application.Wallets;

namespace Marketplace.Host.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — portefeuille : solde à venir (gains en attente de livraison),
/// solde principal (retirable), historique des mouvements et demandes de retrait.
/// Une demande de retrait RETIENT les fonds (statut « Requested ») et attend la
/// VALIDATION d'un administrateur ; le versement FedaPay n'est déclenché qu'à
/// l'approbation admin (ou les fonds sont recrédités en cas de refus).
/// </summary>
public static class SellerWalletEndpoints
{
    public static IEndpointRouteBuilder MapSellerWalletEndpoints(this IEndpointRouteBuilder app)
    {
        var wallet = app.MapSellerGroup("/api/sellers/wallet", "Vendeur · Portefeuille");

        wallet.MapGet("", WalletAsync);
        wallet.MapGet("/transactions", TransactionsAsync);
        wallet.MapGet("/withdrawals", WithdrawalsAsync);
        wallet.MapPost("/withdraw", WithdrawAsync);

        return app;
    }

    /// <summary>Soldes du portefeuille de la boutique (à venir + principal).</summary>
    private static async Task<IResult> WalletAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new GetSellerWalletQuery(seller.Id), ct)).Match(w => Results.Ok(w));
    }

    /// <summary>Historique des mouvements (crédits/débits) du portefeuille.</summary>
    private static async Task<IResult> TransactionsAsync(ClaimsPrincipal user, int? take, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new ListSellerWalletTransactionsQuery(seller.Id, take ?? 50), ct)).Match(t => Results.Ok(t));
    }

    /// <summary>Demandes de retrait de la boutique (statut, référence PSP).</summary>
    private static async Task<IResult> WithdrawalsAsync(ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new ListWithdrawalsQuery(seller.Id), ct)).Match(w => Results.Ok(w));
    }

    /// <summary>
    /// Crée une demande de retrait du solde principal (fonds retenus, en attente de
    /// validation admin — pas de versement immédiat).
    ///
    /// UNE SEULE DEMANDE EN ATTENTE À LA FOIS.
    ///
    /// Cette route n'avait ni clé d'idempotence ni verrou : deux soumissions
    /// rapprochées — un double tap sur un réseau lent, un bouton sans état occupé
    /// côté application — créaient DEUX retenues de fonds distinctes. Si
    /// l'administrateur approuvait les deux, le vendeur était payé deux fois.
    ///
    /// Le refus porte sur l'existence d'une demande NON TRAITÉE. C'est plus simple
    /// qu'une clé d'idempotence et cela correspond au métier : un vendeur n'a aucune
    /// raison d'avoir deux demandes en attente, et la seconde brouillerait la file de
    /// validation.
    ///
    /// Ce n'est pas un verrou transactionnel : deux requêtes rigoureusement
    /// simultanées peuvent encore passer. Fermer complètement la fenêtre demande une
    /// contrainte d'unicité partielle en base — à faire, mais avec une migration.
    /// </summary>
    private static async Task<IResult> WithdrawAsync(ClaimsPrincipal user, WithdrawRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        // ARGENT, et non vente : c'est le SEUL geste de ce fichier qui fait sortir des
        // fonds. On passe donc par le droit de RETRAIT, pas par le droit de vendre —
        // une boutique fermée volontairement doit pouvoir encaisser ce qu'elle a gagné
        // avant de fermer, seule la suspension gèle. Voir SellerRights.CanWithdraw.
        var (seller, denied) = await SellerBff.ResolvePayableSellerAsync(user, sellers, ct);
        if (denied is not null)
        {
            return denied;
        }

        var existing = await sender.Send(new ListWithdrawalsQuery(seller!.Id), ct);
        if (existing.IsSuccess)
        {
            var pending = existing.Value.FirstOrDefault(w =>
                w.Status is "Requested" or "Pending");

            if (pending is not null)
            {
                return Results.Problem(
                    title: "sellers.wallet.withdrawal_already_pending",
                    detail: $"Une demande de retrait de {pending.Amount:0} {pending.Currency} est déjà en cours "
                          + "de traitement. Attendez son issue avant d'en créer une nouvelle.",
                    statusCode: StatusCodes.Status409Conflict);
            }
        }

        return (await sender.Send(new RequestWithdrawalCommand(seller!.Id, request.Amount), ct))
            .Match(w => Results.Ok(w));
    }

    public sealed record WithdrawRequest(decimal Amount);
}
