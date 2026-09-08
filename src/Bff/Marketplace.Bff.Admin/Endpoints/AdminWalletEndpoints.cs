using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Settlement.Application.Wallets;
using Marketplace.Modules.Settlement.Contracts;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>
/// BFF Admin — portefeuille de la plateforme : solde des commissions encaissées
/// et solde des frais de livraison encaissés, plus le grand livre des mouvements.
/// </summary>
public static class AdminWalletEndpoints
{
    public static IEndpointRouteBuilder MapAdminWalletEndpoints(this IEndpointRouteBuilder app)
    {
        var wallet = app.MapAdminGroup("/admin/wallet", "Admin · Portefeuille plateforme");
        wallet.MapGet("", WalletAsync);
        wallet.MapGet("/transactions", TransactionsAsync);
        wallet.MapGet("/withdrawals/pending", PendingWithdrawalsAsync);
        wallet.MapGet("/withdrawals/processing", ProcessingWithdrawalsAsync);
        wallet.MapPost("/withdrawals/{id:guid}/approve", ApproveWithdrawalAsync);
        wallet.MapPost("/withdrawals/{id:guid}/reject", RejectWithdrawalAsync);
        wallet.MapGet("/reconciliation", ReconciliationAsync);
        wallet.MapGet("/withdrawals/customers/pending", PendingCustomerWithdrawalsAsync);

        return app;
    }

    /// <summary>Soldes plateforme : commissions + frais de livraison encaissés.</summary>
    private static async Task<IResult> WalletAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new GetPlatformWalletQuery(), ct)).Match(w => Results.Ok(w));

    /// <summary>Historique des mouvements du portefeuille plateforme.</summary>
    private static async Task<IResult> TransactionsAsync(int? take, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListPlatformWalletTransactionsQuery(take ?? 100), ct)).Match(t => Results.Ok(t));

    /// <summary>Liste des demandes de retrait vendeur en attente de validation.</summary>
    private static async Task<IResult> PendingWithdrawalsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListPendingWithdrawalsQuery(), ct)).Match(list => Results.Ok(list));

    /// <summary>
    /// Versements demandés au PSP et non encore confirmés. À surveiller : tant qu'un
    /// retrait y reste, le vendeur est débité sans avoir reçu son argent.
    /// </summary>
    private static async Task<IResult> ProcessingWithdrawalsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListProcessingWithdrawalsQuery(), ct)).Match(list => Results.Ok(list));

    /// <summary>Approuve une demande de retrait et déclenche le payout FedaPay.</summary>
    private static async Task<IResult> ApproveWithdrawalAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ApproveWithdrawalCommand(id), ct)).Match(view => Results.Ok(view));

    /// <summary>Rejette une demande de retrait avec un motif.</summary>
    private static async Task<IResult> RejectWithdrawalAsync(Guid id, RejectRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new RejectWithdrawalCommand(id, request.Reason), ct)).Match(view => Results.Ok(view));

    /// <summary>
    /// File des retraits de CAGNOTTE CLIENT en attente de validation.
    ///
    /// Séparée de la file vendeur, et pas par commodité d'affichage : ce ne sont pas
    /// les mêmes contrôles. Un retrait vendeur se valide en regardant une boutique et
    /// un compte professionnel ; un retrait client se valide en regardant un compte
    /// particulier, avec ce que cela suppose de vigilance sur le blanchiment — une
    /// cagnotte alimentée par des remboursements puis vidée aussitôt n'a pas la même
    /// lecture qu'un chiffre d'affaires.
    ///
    /// La validation elle-même passe par les routes /approve et /reject déjà en place :
    /// un retrait est un retrait, quel que soit celui qui le demande.
    /// </summary>
    private static async Task<IResult> PendingCustomerWithdrawalsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListPendingCustomerWithdrawalsQuery(), ct)).Match(list => Results.Ok(list));

    /// <summary>
    /// Contrôle comptable : chaque solde de portefeuille est-il exactement égal à la
    /// somme des écritures qui le justifient ?
    ///
    /// Répond 200 quand tout concorde, et 409 CONFLICT dès qu'un écart existe — un code
    /// d'erreur, volontairement, pour qu'une supervision branchée sur cette route sonne
    /// sans avoir à interpréter le corps de la réponse. Le détail des écarts est dans
    /// les deux cas dans le corps.
    /// </summary>
    private static async Task<IResult> ReconciliationAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ReconcileWalletsQuery(), ct))
            .Match(report => report.IsBalanced
                ? Results.Ok(report)
                : Results.Json(report, statusCode: StatusCodes.Status409Conflict));

    /// <summary>Corps de la requête de rejet d'une demande de retrait.</summary>
    public record RejectRequest(string Reason);
}
