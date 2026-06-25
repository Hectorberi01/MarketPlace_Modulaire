using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.AddKybDocument;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.RegisterSeller;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.SetPayoutAccount;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.UpdateSellerProfile;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — boutique : onboarding (devenir vendeur), profil, compte de
/// reversement et pièces KYB. Toutes les opérations sont scopées à la boutique
/// du jeton (résolue côté serveur).
/// </summary>
public static class SellerShopEndpoints
{
    public static IEndpointRouteBuilder MapSellerShopEndpoints(this IEndpointRouteBuilder app)
    {
        var shop = app.MapGroup("/seller/shop").WithTags("Vendeur · Boutique").RequireAuthorization();

        shop.MapGet("/", MyShopAsync);
        shop.MapPost("/", OnboardAsync);
        shop.MapPut("/profile", UpdateProfileAsync);
        shop.MapPut("/payout-account", SetPayoutAccountAsync);
        shop.MapPost("/kyb-documents", AddKybDocumentAsync);

        return app;
    }

    /// <summary>Ma boutique (profil, statut KYB, commission, note).</summary>
    private static async Task<IResult> MyShopAsync(ClaimsPrincipal user, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null ? SellerBff.NotASeller() : Results.Ok(seller);
    }

    /// <summary>Onboarding : crée la boutique rattachée au compte du jeton.</summary>
    private static async Task<IResult> OnboardAsync(ClaimsPrincipal user, OnboardRequest request, ISender sender, CancellationToken ct)
        => SellerBff.CurrentUserId(user) is not { } userId
            ? Results.Unauthorized()
            : (await sender.Send(new RegisterSellerCommand(userId, request.ShopName), ct)).Match(id => Results.Ok(new { sellerId = id }));

    /// <summary>Met à jour le profil de la boutique (nom, logo, description).</summary>
    private static async Task<IResult> UpdateProfileAsync(ClaimsPrincipal user, ProfileRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new UpdateSellerProfileCommand(seller.Id, request.ShopName, request.LogoUrl, request.Description), ct)).Match(() => Results.NoContent());
    }

    /// <summary>Définit le compte de reversement (MoMo, Wave, banque…).</summary>
    private static async Task<IResult> SetPayoutAccountAsync(ClaimsPrincipal user, PayoutAccountRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new SetPayoutAccountCommand(seller.Id, request.Provider, request.AccountNumber, request.AccountName), ct)).Match(() => Results.NoContent());
    }

    /// <summary>Ajoute une pièce KYB (repasse la vérification en revue).</summary>
    private static async Task<IResult> AddKybDocumentAsync(ClaimsPrincipal user, KybDocumentRequest request, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        return seller is null
            ? SellerBff.NotASeller()
            : (await sender.Send(new AddKybDocumentCommand(seller.Id, request.Type, request.FileUrl), ct)).Match(id => Results.Ok(new { documentId = id }));
    }

    public sealed record OnboardRequest(string ShopName);
    public sealed record ProfileRequest(string ShopName, string? LogoUrl, string? Description);
    public sealed record PayoutAccountRequest(string Provider, string AccountNumber, string AccountName);
    public sealed record KybDocumentRequest(string Type, string FileUrl);
}
