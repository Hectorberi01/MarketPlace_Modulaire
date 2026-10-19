using MediatR;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Offers.Application.Offers.Commands.ChangeOfferPrice;
using Marketplace.Modules.Offers.Application.Offers.Commands.ChangeOfferStatus;
using Marketplace.Modules.Offers.Application.Offers.Commands.CreateOffer;
using Marketplace.Modules.Offers.Application.Offers.Commands.SetHandlingTime;
using Marketplace.Modules.Offers.Application.Offers.Queries;

namespace Marketplace.Host.Endpoints;

/// <summary>
/// Endpoints HTTP du module Offers : lecture publique (Buy Box, comparaison) et
/// gestion par le vendeur authentifié.
/// </summary>
public static class OfferEndpoints
{
    /// <summary>Enregistre les routes des offres.</summary>
    public static IEndpointRouteBuilder MapOfferEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapAdminGroup("/api/offers").WithTags("Offers");

        group.MapGet("/{id:guid}", GetAsync).WithName("GetOffer").AllowAnonymous();
        group.MapGet("/by-product/{productId:guid}", ListByProductAsync).WithName("ListOffersByProduct").AllowAnonymous();
        group.MapGet("/by-seller/{sellerId:guid}", ListBySellerAsync).WithName("ListOffersBySeller").RequireAuthorization();

        group.MapPost("/", CreateAsync).WithName("CreateOffer").RequireAuthorization();
        group.MapPut("/{id:guid}/price", ChangePriceAsync).WithName("ChangeOfferPrice").RequireAuthorization();
        group.MapPatch("/{id:guid}/status", ChangeStatusAsync).WithName("ChangeOfferStatus").RequireAuthorization();
        group.MapPut("/{id:guid}/handling-time", SetHandlingTimeAsync).WithName("SetOfferHandlingTime").RequireAuthorization();

        return app;
    }

    /// <summary>Crée une offre (vendeur actif × produit existant, vérifiés in-process).</summary>
    private static async Task<IResult> CreateAsync(CreateOfferRequest request, ISender sender, CancellationToken ct)
    {
        var command = new CreateOfferCommand(
            request.ProductId, request.SellerId, request.Sku, request.SellerPrice, request.Currency,
            request.Condition, request.FulfillmentType, request.ShipFromLocationId, request.HandlingTime);

        var result = await sender.Send(command, ct);
        return result.Match(id => Results.Created($"/api/offers/{id}", new { id }));
    }

    /// <summary>Récupère une offre par son identifiant.</summary>
    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetOfferQuery(id), ct);
        return result.Match(summary => Results.Ok(summary));
    }

    /// <summary>Liste les offres actives d'un produit (Buy Box / comparaison vendeurs).</summary>
    private static async Task<IResult> ListByProductAsync(Guid productId, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListOffersByProductQuery(productId), ct);
        return result.Match(offers => Results.Ok(offers));
    }

    /// <summary>Liste les offres d'un vendeur (back-office vendeur).</summary>
    private static async Task<IResult> ListBySellerAsync(Guid sellerId, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListOffersBySellerQuery(sellerId), ct);
        return result.Match(offers => Results.Ok(offers));
    }

    /// <summary>Change le prix VENDEUR (net) d'une offre ; la plateforme recalcule le prix produit.</summary>
    private static async Task<IResult> ChangePriceAsync(Guid id, PriceRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ChangeOfferPriceCommand(id, request.SellerPrice), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Change le statut d'une offre : Active, Paused ou OutOfStock.</summary>
    private static async Task<IResult> ChangeStatusAsync(Guid id, StatusRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ChangeOfferStatusCommand(id, request.Status), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Modifie le délai de préparation (jours) d'une offre.</summary>
    private static async Task<IResult> SetHandlingTimeAsync(Guid id, HandlingTimeRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new SetHandlingTimeCommand(id, request.HandlingTime), ct);
        return result.Match(() => Results.NoContent());
    }

    public sealed record CreateOfferRequest(
        Guid ProductId, Guid SellerId, string Sku, decimal SellerPrice, string Currency,
        string Condition, string FulfillmentType, Guid ShipFromLocationId, int HandlingTime);

    public sealed record PriceRequest(decimal SellerPrice);
    public sealed record StatusRequest(string Status);
    public sealed record HandlingTimeRequest(int HandlingTime);
}
