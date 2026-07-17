using MediatR;
using System.Linq;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Cart.Application.Carts.Commands;
using Marketplace.Modules.Cart.Application.Carts.Commands.AddItem;
using Marketplace.Modules.Cart.Application.Carts.Queries;
using Marketplace.Modules.Catalog.Contracts;

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

        cart.MapPost("/coupon", ApplyCouponAsync);
        cart.MapDelete("/coupon", RemoveCouponAsync);

        return app;
    }

    /// <summary>
    /// Attache un code promo au PANIER, puis renvoie le panier revalorisé.
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// CETTE ROUTE MENTAIT À L'ACHETEUR. C'EST LA CORRECTION LA PLUS IMPORTANTE DU LOT.
    ///
    /// Avant, elle calculait une remise, répondait « Code promo appliqué. » avec un total
    /// réduit… et NE PERSISTAIT RIEN. Le commentaire s'en justifiait — « sans état : le
    /// client ré-applique le code au paiement » — mais AUCUN chemin de paiement n'acceptait
    /// de code : ni le panier, ni la commande, ni le checkout n'avaient de champ pour le
    /// recevoir.
    ///
    /// L'acheteur voyait donc s'afficher une réduction qu'il ne recevrait jamais, puis
    /// payait le plein tarif. Ce n'est pas une fonctionnalité manquante : c'est un prix
    /// affiché faux. Le pire genre de bug — celui qui a l'air de marcher.
    ///
    /// Désormais le code est ATTACHÉ au panier, transporté jusqu'à la commande, et
    /// réellement décompté à la confirmation du paiement.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> ApplyCouponAsync(
        ClaimsPrincipal user, CouponRequest request, ISender sender, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var applied = await sender.Send(new ApplyCouponCommand(buyerId, request.Code ?? string.Empty), ct);
        if (applied.IsFailure)
        {
            // Le message vient du domaine (code invalide, expiré, déjà utilisé) : il est
            // destiné à être affiché tel quel.
            return applied.Match(() => Results.NoContent());
        }

        // On renvoie le panier REVALORISÉ, pas un simple 204 : le client doit voir le
        // nouveau total immédiatement, et ce total doit être celui qu'il paiera vraiment.
        var cart = await sender.Send(new GetActiveCartQuery(buyerId), ct);
        return cart.Match(c => Results.Ok(new
        {
            promotionCode = c.PromotionCode,
            subtotal = c.Subtotal,
            totalSellerDiscount = c.TotalSellerDiscount,
            totalPlatformDiscount = c.TotalPlatformDiscount,
            grandTotal = c.GrandTotal,
            currency = c.Currency,
        }));
    }

    /// <summary>Retire le code promo du panier et renvoie le panier revalorisé.</summary>
    private static async Task<IResult> RemoveCouponAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
    {
        if (MobileBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var removed = await sender.Send(new RemoveCouponCommand(buyerId), ct);
        if (removed.IsFailure)
        {
            return removed.Match(() => Results.NoContent());
        }

        var cart = await sender.Send(new GetActiveCartQuery(buyerId), ct);
        return cart.Match(c => Results.Ok(new
        {
            promotionCode = c.PromotionCode,
            subtotal = c.Subtotal,
            grandTotal = c.GrandTotal,
            currency = c.Currency,
        }));
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

            // Le code attaché au panier : l'app peut ainsi l'afficher (« BIENVENUE10
            // appliqué ») et proposer de le retirer, au lieu de l'oublier au moindre
            // rechargement d'écran.
            promotionCode = cart.PromotionCode,
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
