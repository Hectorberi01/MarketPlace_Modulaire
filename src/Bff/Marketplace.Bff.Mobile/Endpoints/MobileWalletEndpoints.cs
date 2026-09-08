using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Settlement.Application.Wallets;

namespace Marketplace.Bff.Mobile.Endpoints;

/// <summary>
/// BFF Mobile — LA CAGNOTTE DE L'ACHETEUR.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE QUE CES TROIS ROUTES CHANGENT POUR LE CLIENT.
///
/// Un remboursement de retour était, jusqu'ici, une promesse : la demande passait
/// « remboursement validé », puis il ne se passait plus rien de visible tant qu'un
/// administrateur n'avait pas effectué le versement à la main chez FedaPay. Le client
/// n'avait aucun endroit où vérifier où en était SON argent.
///
/// La cagnotte est cet endroit. Le crédit y apparaît en quelques secondes, le relevé
/// dit d'où vient chaque ligne, et le retrait redevient une décision du client plutôt
/// qu'une tâche d'exploitation.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class MobileWalletEndpoints
{
    public static IEndpointRouteBuilder MapMobileWalletEndpoints(this IEndpointRouteBuilder app)
    {
        var wallet = app.MapGroup("/mobile/wallet").WithTags("Mobile · Cagnotte").RequireAuthorization();

        wallet.MapGet("/", BalanceAsync);
        wallet.MapGet("/transactions", TransactionsAsync);
        wallet.MapGet("/withdrawals", WithdrawalsAsync);
        wallet.MapPost("/withdrawals", RequestWithdrawalAsync);

        return app;
    }

    /// <summary>Solde de la cagnotte : disponible, engagé, et retraits en route.</summary>
    private static async Task<IResult> BalanceAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } customerId
            ? Results.Unauthorized()
            : (await sender.Send(new GetCustomerWalletQuery(customerId), ct)).Match(w => Results.Ok(w));

    /// <summary>
    /// Relevé des mouvements. Chaque ligne porte son motif et sa référence : c'est ce
    /// qui permet au client de rattacher un crédit à un retour précis, sans avoir à
    /// écrire au support pour demander « c'est quoi ces 12 000 F ».
    /// </summary>
    private static async Task<IResult> TransactionsAsync(ClaimsPrincipal user, int? take, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } customerId
            ? Results.Unauthorized()
            : (await sender.Send(new ListCustomerWalletTransactionsQuery(customerId, take ?? 50), ct))
                .Match(t => Results.Ok(t));

    /// <summary>Historique des demandes de retrait de ce client.</summary>
    private static async Task<IResult> WithdrawalsAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } customerId
            ? Results.Unauthorized()
            : (await sender.Send(new ListCustomerWithdrawalsQuery(customerId), ct)).Match(w => Results.Ok(w));

    /// <summary>
    /// Demande de versement de la cagnotte sur le Mobile Money du client.
    ///
    /// Les fonds sont RETENUS immédiatement (ils quittent le disponible) mais rien
    /// n'est versé avant validation admin : c'est la même règle que pour un vendeur,
    /// et pour la même raison — un versement sortant ne se déclenche pas sur la seule
    /// foi d'un appel HTTP.
    ///
    /// L'identifiant du client vient du JETON, jamais du corps de la requête : accepter
    /// un identifiant fourni par l'appelant reviendrait à laisser n'importe qui vider
    /// la cagnotte de n'importe qui.
    /// </summary>
    private static async Task<IResult> RequestWithdrawalAsync(
        ClaimsPrincipal user, WithdrawRequest request, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } customerId
            ? Results.Unauthorized()
            : (await sender.Send(new RequestCustomerWithdrawalCommand(customerId, request.Amount), ct))
                .Match(w => Results.Ok(w));

    /// <summary>Corps de la demande de retrait : le montant, et rien d'autre.</summary>
    public record WithdrawRequest(decimal Amount);
}
