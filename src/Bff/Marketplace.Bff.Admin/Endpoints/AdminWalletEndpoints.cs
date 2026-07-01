using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Settlement.Application.Wallets;

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

        return app;
    }

    /// <summary>Soldes plateforme : commissions + frais de livraison encaissés.</summary>
    private static async Task<IResult> WalletAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new GetPlatformWalletQuery(), ct)).Match(w => Results.Ok(w));

    /// <summary>Historique des mouvements du portefeuille plateforme.</summary>
    private static async Task<IResult> TransactionsAsync(int? take, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListPlatformWalletTransactionsQuery(take ?? 100), ct)).Match(t => Results.Ok(t));
}
