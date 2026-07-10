using System.Security.Claims;
using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.ActivateSeller;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.AddKybDocument;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.ApproveKyb;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.RegisterSeller;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.RejectKyb;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.SetPayoutAccount;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.SuspendSeller;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.UpdateSellerProfile;
using Marketplace.Modules.Sellers.Application.Sellers.Queries.GetSeller;
using Marketplace.Modules.Sellers.Application.Sellers.Queries.GetSellerByUser;
using Marketplace.Modules.Sellers.Application.Sellers.Queries.ListSellers;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Sellers : espace vendeur (authentifié, agit sur sa
/// propre boutique) et administration (Admin : KYB, activation, suspension).
/// </summary>
public static class SellerEndpoints
{
    /// <summary>Enregistre les routes vendeur (self-service) et administration.</summary>
    public static IEndpointRouteBuilder MapSellerEndpoints(this IEndpointRouteBuilder app)
    {
        MapSelfService(app);
        MapAdmin(app);
        return app;
    }

    // ------------------------------------------------------------ Self-service

    private static void MapSelfService(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sellers").WithTags("Sellers · Boutique").RequireAuthorization();

        group.MapPost("/", OnboardAsync).WithName("OnboardSeller");
        group.MapGet("/me", GetMyShopAsync).WithName("GetMyShop");
        group.MapPut("/me", UpdateMyShopAsync).WithName("UpdateMyShop");
        group.MapPut("/me/payout", SetMyPayoutAsync).WithName("SetMyPayout");
        group.MapPost("/me/kyb", AddMyKybDocumentAsync).WithName("AddMyKybDocument");
    }

    /// <summary>Onboarde le compte authentifié comme vendeur (boutique en attente).</summary>
    private static async Task<IResult> OnboardAsync(OnboardRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new RegisterSellerCommand(userId, request.ShopName), ct);
        return result.Match(id => Results.Created($"/api/sellers/{id}", new { id }));
    }

    /// <summary>Renvoie la boutique du vendeur authentifié.</summary>
    private static async Task<IResult> GetMyShopAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new GetSellerByUserQuery(userId), ct);
        return result.Match(summary => Results.Ok(summary));
    }

    /// <summary>Met à jour la boutique du vendeur authentifié.</summary>
    private static async Task<IResult> UpdateMyShopAsync(UpdateShopRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var shop = await sender.Send(new GetSellerByUserQuery(userId), ct);
        if (shop.IsFailure)
        {
            return shop.Match(_ => Results.NoContent());
        }

        var result = await sender.Send(
            new UpdateSellerProfileCommand(shop.Value.Id, request.ShopName, request.LogoUrl, request.Description), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Définit les coordonnées de reversement du vendeur authentifié.</summary>
    private static async Task<IResult> SetMyPayoutAsync(PayoutRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var shop = await sender.Send(new GetSellerByUserQuery(userId), ct);
        if (shop.IsFailure)
        {
            return shop.Match(_ => Results.NoContent());
        }

        var result = await sender.Send(
            new SetPayoutAccountCommand(shop.Value.Id, request.Provider, request.AccountNumber, request.AccountName), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Ajoute une pièce KYB à la boutique du vendeur authentifié.</summary>
    private static async Task<IResult> AddMyKybDocumentAsync(KybRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var shop = await sender.Send(new GetSellerByUserQuery(userId), ct);
        if (shop.IsFailure)
        {
            return shop.Match(_ => Results.NoContent());
        }

        var result = await sender.Send(new AddKybDocumentCommand(shop.Value.Id, request.Type, request.FileUrl), ct);
        return result.Match(id => Results.Created($"/api/sellers/{shop.Value.Id}", new { documentId = id }));
    }

    // ------------------------------------------------------------------ Admin

    private static void MapAdmin(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sellers/admin")
            .WithTags("Sellers · Admin")
            .RequireAuthorization(policy => policy.RequireRole("Admin"));

        group.MapGet("/", ListAsync).WithName("ListSellers");
        group.MapGet("/{id:guid}", GetAsync).WithName("GetSeller");
        group.MapPost("/{id:guid}/kyb/approve", ApproveKybAsync).WithName("ApproveKyb");
        group.MapPost("/{id:guid}/kyb/reject", RejectKybAsync).WithName("RejectKyb");
        group.MapPost("/{id:guid}/activate", ActivateAsync).WithName("ActivateSeller");
        group.MapPost("/{id:guid}/suspend", SuspendAsync).WithName("SuspendSeller");
    }

    /// <summary>Liste les vendeurs (Admin).</summary>
    private static async Task<IResult> ListAsync(ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListSellersQuery(), ct);
        return result.Match(sellers => Results.Ok(sellers));
    }

    /// <summary>Récupère un vendeur par son identifiant (Admin).</summary>
    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetSellerQuery(id), ct);
        return result.Match(summary => Results.Ok(summary));
    }

    /// <summary>Valide le KYB d'un vendeur (Admin).</summary>
    private static async Task<IResult> ApproveKybAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ApproveKybCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Rejette le KYB d'un vendeur (Admin).</summary>
    private static async Task<IResult> RejectKybAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new RejectKybCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Active un vendeur, KYB validé et payout requis (Admin).</summary>
    private static async Task<IResult> ActivateAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ActivateSellerCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Suspend un vendeur (Admin).</summary>
    private static async Task<IResult> SuspendAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new SuspendSellerCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    // -------------------------------------------------------------- Utilitaire

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    // ------------------------------------------------------- Contrats d'entrée

    public sealed record OnboardRequest(string ShopName);
    public sealed record UpdateShopRequest(string ShopName, string? LogoUrl, string? Description);
    public sealed record PayoutRequest(string Provider, string AccountNumber, string AccountName);
    public sealed record KybRequest(string Type, string FileUrl);
}
