using System.Security.Claims;
using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Cart.Application.Carts.Commands;
using Marketplace.Modules.Cart.Application.Carts.Commands.AddItem;
using Marketplace.Modules.Cart.Application.Carts.Queries;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Cart : le panier de l'acheteur connecté. Les lignes
/// sont valorisées à la lecture via Pricing (prix de base + promotions).
///
/// LA VALIDATION DU PANIER N'EST PAS ICI. Elle vit dans le BFF Client
/// (`POST /client/checkout/pay`), qui crée la commande ; le panier est ensuite fermé
/// PAR RÉACTION à l'événement `OrderPlaced`. Les routes ci-dessous ne font que LIRE
/// et MODIFIER un panier ouvert — voir le commentaire sur le groupe de routes.
/// </summary>
public static class CartEndpoints
{
    /// <summary>Enregistre les routes du panier (acheteur authentifié).</summary>
    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder app)
    {
        var cart = app.MapAuthenticatedGroup("/api/cart").WithTags("Cart");

        cart.MapGet("/", GetAsync).WithName("GetCart");
        cart.MapPost("/items", AddItemAsync).WithName("AddCartItem");
        cart.MapPut("/items/{offerId:guid}", UpdateItemAsync).WithName("UpdateCartItem");
        cart.MapDelete("/items/{offerId:guid}", RemoveItemAsync).WithName("RemoveCartItem");
        cart.MapDelete("/", ClearAsync).WithName("ClearCart");

        // ════════════════════════════════════════════════════════════════════════
        // LA VALIDATION DU PANIER N'EST PLUS ICI. `POST /api/cart/checkout` A ÉTÉ SUPPRIMÉ.
        //
        // Cette route DÉTRUISAIT le panier de l'acheteur sans rien créer en échange. Elle
        // envoyait `CheckoutCartCommand`, dont le seul effet était `Cart.MarkCheckedOut()` :
        // le panier passait `CheckedOut`, et la seule lecture du panier actif filtre sur
        // `Active` (`CartRepository.GetActiveByBuyerAsync`). Or `Cart` n'expose AUCUNE
        // méthode de réouverture, et `CartCheckedOutIntegrationEvent` n'a aucun consommateur
        // dans le dépôt — ses deux seules occurrences sont le contrat et le publieur. Un
        // appel, et l'acheteur perdait ses articles DÉFINITIVEMENT, sans qu'aucune commande
        // n'existe et sans le moindre recours : ni réouverture, ni réaction en aval.
        //
        // POURQUOI SUPPRIMER PLUTÔT QUE RÉPARER.
        //
        // « Valider son panier » n'est pas un geste isolé : c'est le premier pas d'un achat,
        // qui doit créer la commande et engager le paiement dans le même mouvement. Une
        // route qui ferme le panier SANS commande ne peut être que perdante — au mieux elle
        // ne sert à rien, au pire elle vole le panier. Il n'y a donc rien à réparer : il
        // n'existe aucun état sain où fermer un panier à vide ait un sens.
        //
        // Le vrai parcours d'achat est `POST /client/checkout/pay` (BFF Client) : il crée la
        // commande, et le panier est fermé ENSUITE, par réaction à `OrderPlaced`
        // (`CloseCartOnOrderPlacedHandler`). La fermeture reste ainsi une CONSÉQUENCE de la
        // commande, jamais une action que l'acheteur puisse déclencher seule.
        // ════════════════════════════════════════════════════════════════════════

        return app;
    }

    /// <summary>Renvoie le panier actif valorisé de l'acheteur connecté.</summary>
    private static async Task<IResult> GetAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new GetActiveCartQuery(buyerId), ct);
        return result.Match(cart => Results.Ok(cart));
    }

    /// <summary>Ajoute une offre au panier (création implicite du panier si besoin).</summary>
    private static async Task<IResult> AddItemAsync(AddItemRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new AddItemToCartCommand(buyerId, request.OfferId, request.Quantity), ct);
        return result.Match(cartId => Results.Ok(new { cartId }));
    }

    /// <summary>Change la quantité d'une ligne (0 la retire).</summary>
    private static async Task<IResult> UpdateItemAsync(Guid offerId, UpdateItemRequest request, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new UpdateCartItemQuantityCommand(buyerId, offerId, request.Quantity), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Retire une ligne du panier.</summary>
    private static async Task<IResult> RemoveItemAsync(Guid offerId, ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new RemoveCartItemCommand(buyerId, offerId), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Vide le panier actif.</summary>
    private static async Task<IResult> ClearAsync(ClaimsPrincipal principal, ISender sender, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        var result = await sender.Send(new ClearCartCommand(buyerId), ct);
        return result.Match(() => Results.NoContent());
    }

    private static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    public sealed record AddItemRequest(Guid OfferId, int Quantity);
    public sealed record UpdateItemRequest(int Quantity);
}
