using MediatR;
using System.Linq;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Cart.Application.Carts.Commands;
using Marketplace.Modules.Cart.Application.Carts.Commands.AddItem;
using Marketplace.Modules.Cart.Application.Carts.Queries;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Pricing.Contracts;

namespace Marketplace.Bff.Mobile.Endpoints;

/// <summary>BFF Mobile — panier (/mobile/cart/*). Scopé à l'acheteur du jeton.</summary>
public static class MobileCartEndpoints
{
    public static IEndpointRouteBuilder MapMobileCartEndpoints(this IEndpointRouteBuilder app)
    {
        var cart = app.MapGroup("/mobile/cart").WithTags("Mobile · Panier").RequireAuthorization();

        cart.MapGet("/", GetAsync);
        cart.MapPost("/items", AddItemAsync);
        cart.MapPatch("/items/{offerId:guid}", UpdateItemAsync);
        cart.MapDelete("/items/{offerId:guid}", RemoveItemAsync);

        // Code promo : validation via le moteur Pricing (sans état serveur).
        cart.MapPost("/coupon", ApplyCouponAsync);
        cart.MapDelete("/coupon", () => Results.NoContent());

        return app;
    }

    /// <summary>
    /// Valide un code promo sur le panier courant via le moteur Pricing et renvoie
    /// la remise applicable. Sans état : le client ré-applique le code au paiement.
    /// </summary>
    private static async Task<IResult> ApplyCouponAsync(
        ClaimsPrincipal user, CouponRequest request, ISender sender, IPricingModuleApi pricing, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var code = request.Code?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            return Results.BadRequest(new { error = "coupon_required" });
        }

        var cartResult = await sender.Send(new GetActiveCartQuery(buyerId), ct);
        if (cartResult.IsFailure)
        {
            return Results.BadRequest(new { error = "cart_unavailable" });
        }

        var cart = cartResult.Value;
        if (cart.Lines.Count == 0)
        {
            return Results.BadRequest(new { error = "cart_empty" });
        }

        // Le code global s'applique au sous-total ; on fournit une ligne de
        // référence pour les promos ciblées (catégorie/vendeur/produit).
        var first = cart.Lines[0];
        var breakdown = await pricing.CalculatePriceAsync(new PriceRequest(
            BaseAmount: cart.Subtotal,
            Currency: cart.Currency,
            ProductId: first.ProductId,
            CategoryId: first.CategoryId,
            SellerId: first.SellerId,
            Quantity: 1,
            Subtotal: cart.Subtotal,
            Code: code,
            IsFirstOrder: false), ct);

        var discount = cart.Subtotal - breakdown.FinalAmount;
        var valid = discount > 0m;

        return Results.Ok(new
        {
            valid,
            code = code.ToUpperInvariant(),
            discount = valid ? discount : 0m,
            subtotal = cart.Subtotal,
            total = valid ? breakdown.FinalAmount : cart.Subtotal,
            currency = cart.Currency,
            message = valid ? "Code promo appliqué." : "Code promo invalide ou expiré.",
        });
    }

    public sealed record CouponRequest(string Code);

    /// <summary>
    /// Panier courant valorisé (multi-vendeur, sous-total, promos appliquées),
    /// enrichi avec le nom et l'image principale de chaque produit (lus dans
    /// Catalog) car le résumé panier ne porte que le SKU.
    /// </summary>
    private static async Task<IResult> GetAsync(ClaimsPrincipal user, ISender sender, ICatalogModuleApi catalog, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new GetActiveCartQuery(buyerId), ct);
        if (result.IsFailure)
        {
            return result.Match(c => Results.Ok(c));
        }

        var cart = result.Value;

        // Séquentiel : le DbContext (scoped) de Catalog n'est pas thread-safe.
        var lines = new List<object>(cart.Lines.Count);
        foreach (var l in cart.Lines)
        {
            string? name = null;
            string? imageUrl = null;
            var product = await catalog.GetProductAsync(l.ProductId, ct);
            if (product is not null)
            {
                name = product.Name;
                var primary = product.Media.FirstOrDefault(m => m.IsPrimary) ?? product.Media.FirstOrDefault();
                imageUrl = primary?.Url;
            }

            lines.Add(new
            {
                offerId = l.OfferId,
                productId = l.ProductId,
                sku = l.Sku,
                productName = name ?? l.Sku,
                imageUrl,
                quantity = l.Quantity,
                unitBaseAmount = l.UnitBaseAmount,
                finalUnitPrice = l.FinalUnitPrice,
                lineTotal = l.LineTotal,
                currency = l.Currency,
            });
        }

        return Results.Ok(new
        {
            cartId = cart.CartId,
            buyerId = cart.BuyerId,
            currency = cart.Currency,
            status = cart.Status,
            lines,
            subtotal = cart.Subtotal,
            totalSellerDiscount = cart.TotalSellerDiscount,
            totalPlatformDiscount = cart.TotalPlatformDiscount,
            grandTotal = cart.GrandTotal,
        });
    }

    /// <summary>Ajoute une offre au panier.</summary>
    private static async Task<IResult> AddItemAsync(ClaimsPrincipal user, AddItemRequest request, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new AddItemToCartCommand(buyerId, request.OfferId, request.Quantity), ct)).Match(id => Results.Ok(new { cartId = id }));

    /// <summary>Modifie la quantité d'une ligne.</summary>
    private static async Task<IResult> UpdateItemAsync(ClaimsPrincipal user, Guid offerId, QuantityRequest request, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new UpdateCartItemQuantityCommand(buyerId, offerId, request.Quantity), ct)).Match(() => Results.NoContent());

    /// <summary>Retire une ligne du panier.</summary>
    private static async Task<IResult> RemoveItemAsync(ClaimsPrincipal user, Guid offerId, ISender sender, CancellationToken ct)
        => MobileBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new RemoveCartItemCommand(buyerId, offerId), ct)).Match(() => Results.NoContent());

    public sealed record AddItemRequest(Guid OfferId, int Quantity);
    public sealed record QuantityRequest(int Quantity);
}
