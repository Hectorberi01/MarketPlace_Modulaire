using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Settlement.Application.Wallets;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — portefeuille : solde à venir (gains en attente de livraison),
/// solde principal (retirable), historique des mouvements et demandes de retrait.
/// Un retrait déclenche un versement FedaPay immédiat vers le compte du vendeur.
/// </summary>
public static class SellerWalletEndpoints
{
    public static IEndpointRouteBuilder MapSellerWalletEndpoints(this IEndpointRouteBuilder app)
    {
        var wallet = app.MapGroup("/seller/wallet").WithTags("Vendeur · Portefeuille").RequireAuthorization();

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

    /// <summary>Demande un retrait du solde principal (versement FedaPay immédiat).</summary>
    private static async Task<IResult> WithdrawAsync(ClaimsPrincipal user, WithdrawRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new RequestWithdrawalCommand(seller.Id, request.Amount), ct)).Match(w => Results.Ok(w));
    }

    public sealed record WithdrawRequest(decimal Amount);
}
