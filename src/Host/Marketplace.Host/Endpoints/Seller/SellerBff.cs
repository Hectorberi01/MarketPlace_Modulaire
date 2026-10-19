using System.Security.Claims;
using MediatR;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Offers.Application.Offers.Queries;
using Marketplace.Modules.Inventory.Application.Locations.Queries;
using Marketplace.Modules.Shipping.Application.Shipments.Queries;
using Marketplace.Modules.Returns.Application.Returns;
using Marketplace.Modules.Reviews.Application.Reviews.Queries;

namespace Marketplace.Host.Seller.Endpoints;

/// <summary>
/// Utilitaires partagés des endpoints /seller. La boutique (SellerId) n'est jamais
/// fournie par le client : elle est résolue à partir de l'utilisateur du jeton.
/// Les helpers « Owns… » verrouillent les mutations adressées par id : on vérifie
/// que la ressource appartient bien à la boutique du token avant d'agir (anti-IDOR).
/// </summary>
internal static class SellerBff
{
    /// <summary>
    /// Rôle exigé sur TOUTE la surface /seller (sauf /seller/auth, anonyme).
    ///
    /// Ce contrôle n'est pas redondant avec <see cref="ResolveSellerAsync"/> — il
    /// le rend simplement inutile comme barrière de sécurité, ce qu'il n'aurait
    /// jamais dû être.
    ///
    /// Les quatre BFF partagent le MÊME Issuer, la MÊME Audience et la MÊME clé
    /// de signature. Un jeton obtenu sur /mobile/auth/login — donc par n'importe
    /// quel acheteur — était par conséquent accepté tel quel ici : les groupes ne
    /// demandaient qu'un `RequireAuthorization()` sans rôle. Rien ne fuyait, car
    /// `ResolveSellerAsync` ne trouvait aucune boutique et l'endpoint répondait
    /// 403. Mais cette protection reposait sur la discipline de CHAQUE endpoint :
    /// le premier qui oubliait d'appeler le résolveur ouvrait le trou en silence.
    ///
    /// La barrière doit être à la porte, pas dans chaque pièce.
    /// </summary>
    public const string SellerRole = "Seller";

    /// <summary>
    /// Groupe protégé de la surface vendeur : authentifié ET porteur du rôle Seller.
    ///
    /// Le rôle est attribué à la création du compte par la console d'administration,
    /// en même temps que la boutique. Aucun vendeur légitime n'est donc laissé
    /// dehors par ce verrou — y compris sur POST /seller/shop.
    /// </summary>
    public static RouteGroupBuilder MapSellerGroup(this IEndpointRouteBuilder app, string prefix, string tag)
        => app.MapGroup(prefix).WithTags(tag).RequireAuthorization(p => p.RequireRole(SellerRole));

    /// <summary>Taille de page par défaut quand seul <c>page</c> est fourni.</summary>
    public const int DefaultPageSize = 30;

    /// <summary>
    /// Plafond ABSOLU. Sans lui, un client pourrait demander pageSize=100000 et
    /// annuler tout le bénéfice de la pagination — voire saturer la mémoire.
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// Découpe une liste déjà triée. Aucun paramètre = liste entière : les
    /// appelants existants (dashboard MAUI) ne cassent pas.
    ///
    /// Le découpage se fait EN MÉMOIRE, après remontée complète depuis le module.
    /// Cela protège le réseau mobile et divise les appels inter-modules, mais PAS
    /// la base : la vraie pagination devra descendre dans les requêtes des modules.
    /// C'est un compromis assumé, pas un oubli.
    /// </summary>
    public static List<T> Paginate<T>(List<T> items, int? page, int? pageSize)
    {
        if (page is null && pageSize is null)
        {
            return items;
        }

        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
        var index = Math.Max(page ?? 1, 1);

        return items.Skip((index - 1) * size).Take(size).ToList();
    }

    /// <summary>Identifiant de l'utilisateur courant, extrait du JWT.</summary>
    public static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>Boutique rattachée à l'utilisateur du jeton (null s'il n'est pas vendeur).</summary>
    public static async Task<SellerSummary?> ResolveSellerAsync(
        ClaimsPrincipal principal, ISellerModuleApi sellers, CancellationToken ct)
    {
        if (CurrentUserId(principal) is not { } userId)
        {
            return null;
        }

        return await sellers.GetSellerByUserIdAsync(userId, ct);
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LE POINT DE PASSAGE DU DROIT DE VENDRE.
    ///
    /// Résout la boutique du jeton ET vérifie qu'elle a le droit d'écrire. Renvoie
    /// soit la boutique, soit la réponse à rendre — jamais les deux, jamais aucun.
    ///
    /// `ResolveSellerAsync` rendait la boutique QUEL QUE SOIT SON STATUT, et aucune
    /// surface d'écriture ne regardait ce statut : un vendeur suspendu conservait
    /// tous ses droits de modification. Cette méthode existe pour qu'il n'y ait plus
    /// qu'un geste à faire, et pour que l'oubli se voie — un appelant qui utilise
    /// encore `ResolveSellerAsync` sur une écriture saute visiblement une étape.
    ///
    /// La règle elle-même vit dans <see cref="SellerRights"/>, côté contrats : le BFF
    /// et les modules doivent l'appliquer à l'identique, et une règle recopiée finit
    /// toujours par diverger.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static async Task<(SellerSummary? Seller, IResult? Denied)> ResolveSellingSellerAsync(
        ClaimsPrincipal principal, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await ResolveSellerAsync(principal, sellers, ct);
        if (seller is null)
        {
            return (null, NotASeller());
        }

        return SellerRights.CanSell(seller.Status)
            ? (seller, null)
            : (null, ShopNotAllowed(seller.Status));
    }

    /// <summary>
    /// Variante pour l'ARGENT. Une boutique fermée volontairement garde le droit de
    /// se faire payer ce qu'elle a gagné ; seule la suspension le suspend.
    /// </summary>
    public static async Task<(SellerSummary? Seller, IResult? Denied)> ResolvePayableSellerAsync(
        ClaimsPrincipal principal, ISellerModuleApi sellers, CancellationToken ct)
    {
        var seller = await ResolveSellerAsync(principal, sellers, ct);
        if (seller is null)
        {
            return (null, NotASeller());
        }

        return SellerRights.CanWithdraw(seller.Status)
            ? (seller, null)
            : (null, ShopNotAllowed(seller.Status));
    }

    /// <summary>
    /// 403 motivé : la boutique existe, mais son état lui interdit cette action.
    ///
    /// 403 et non 404 : contrairement aux ressources d'autrui, il n'y a rien à cacher
    /// ici — le vendeur connaît sa propre boutique, et lui répondre « introuvable »
    /// l'enverrait chercher un problème qui n'existe pas. Le statut est renvoyé pour
    /// que l'application puisse afficher le bon écran.
    /// </summary>
    public static IResult ShopNotAllowed(string? status)
        => Results.Json(
            new { error = "shop_not_allowed", status, message = SellerRights.DenialReason(status) },
            statusCode: StatusCodes.Status403Forbidden);

    /// <summary>403 : l'utilisateur authentifié n'a pas (encore) de boutique.</summary>
    public static IResult NotASeller()
        => Results.Json(new { error = "not_a_seller", hint = "Onboardez la boutique via POST /seller/shop." },
            statusCode: StatusCodes.Status403Forbidden);

    /// <summary>404 normalisé : ressource inexistante OU n'appartenant pas au vendeur (on ne distingue pas, anti-énumération).</summary>
    public static IResult NotFoundOrForbidden(string resource)
        => Results.NotFound(new { error = $"{resource}_not_found" });

    /// <summary>Réponse 501 normalisée pour une capacité non encore exposée par un module.</summary>
    public static IResult NotImplemented(string capability)
        => Results.Json(new { error = "not_implemented", capability }, statusCode: StatusCodes.Status501NotImplemented);

    // --- Vérifications de propriété (anti-IDOR) ---

    /// <summary>
    /// Cette localisation d'expédition appartient-elle bien à la boutique ?
    ///
    /// ATTENTION : ÉCHOUE FERMÉ : si la liste des lieux ne peut pas être obtenue, on répond
    /// « non ». Traiter l'incertitude comme une autorisation, c'est ouvrir la porte
    /// précisément le jour où quelque chose ne va pas.
    /// </summary>
    public static async Task<bool> OwnsLocationAsync(ISender sender, Guid sellerId, Guid locationId, CancellationToken ct)
    {
        if (locationId == Guid.Empty)
        {
            return false;
        }

        var locations = await sender.Send(new ListFulfillmentLocationsQuery(sellerId), ct);
        return locations.IsSuccess && locations.Value.Any(l => l.Id == locationId);
    }

    /// <summary>Vrai si l'offre existe ET appartient au vendeur.</summary>
    public static async Task<bool> OwnsOfferAsync(ISender sender, Guid sellerId, Guid offerId, CancellationToken ct)
    {
        var offer = await sender.Send(new GetOfferQuery(offerId), ct);
        return offer.IsSuccess && offer.Value.SellerId == sellerId;
    }

    /// <summary>Vrai si le produit existe ET appartient au vendeur.</summary>
    public static async Task<bool> OwnsProductAsync(ICatalogModuleApi catalog, Guid sellerId, Guid productId, CancellationToken ct)
    {
        var product = await catalog.GetProductAsync(productId, ct);
        return product is not null && product.SellerId == sellerId;
    }

    /// <summary>Vrai si l'expédition existe ET appartient au vendeur.</summary>
    public static async Task<bool> OwnsShipmentAsync(ISender sender, Guid sellerId, Guid shipmentId, CancellationToken ct)
    {
        var shipment = await sender.Send(new GetShipmentQuery(shipmentId), ct);
        return shipment.IsSuccess && shipment.Value.SellerId == sellerId;
    }

    /// <summary>Vrai si la demande de retour existe ET appartient au vendeur.</summary>
    public static async Task<bool> OwnsReturnAsync(ISender sender, Guid sellerId, Guid returnId, CancellationToken ct)
    {
        var rma = await sender.Send(new GetReturnQuery(returnId), ct);
        return rma.IsSuccess && rma.Value.SellerId == sellerId;
    }

    /// <summary>Vrai si l'avis cible un produit du vendeur.</summary>
    public static async Task<bool> OwnsReviewAsync(ISender sender, Guid sellerId, Guid reviewId, CancellationToken ct)
    {
        var review = await sender.Send(new GetReviewQuery(reviewId), ct);
        return review.IsSuccess && review.Value.SellerId == sellerId;
    }

    /// <summary>
    /// Vrai si le vendeur a au moins une expédition rattachée à la commande (preuve
    /// d'implication).
    ///
    /// ═════════════════════════════════════════════════════════════════════════════
    /// CE CONTRÔLE EST PAR COMMANDE, PAS PAR VENDEUR — ET C'EST UN TROU CONNU.
    ///
    /// Sur une commande MULTI-VENDEURS, le vendeur A passe donc le contrôle pour un
    /// litige qui porte sur la ligne du vendeur B : il en lit tout le fil, et il peut
    /// y ÉCRIRE. Un vendeur tiers peut déposer une pièce dans le dossier d'arbitrage
    /// d'un concurrent.
    ///
    /// CE N'EST PAS UN OUBLI, ET C'EST POURQUOI IL N'EST PAS CORRIGÉ ICI. Le module
    /// Litiges ne connaît pas la notion de vendeur : un `Dispute` porte un `OrderId`,
    /// un `RaisedBy`, un type, un statut et une résolution — aucun rattachement à une
    /// LIGNE de commande ni à une boutique. Il n'existe donc, à ce niveau, AUCUNE
    /// notion plus fine que « impliqué dans la commande » à laquelle se raccrocher. La
    /// console assume d'ailleurs la conséquence en refusant d'écrire « Votre
    /// boutique » sur un message.
    ///
    /// FERMER CE TROU SUPPOSE DE RATTACHER LE LITIGE À UNE LIGNE DE COMMANDE — donc
    /// une migration, la reprise des litiges existants, et une décision sur les
    /// litiges qui portent sur la commande entière (livraison, paiement) plutôt que
    /// sur un article. C'est un chantier de module, pas un correctif de garde, et le
    /// faire à moitié ici donnerait l'illusion d'un cloisonnement qui n'existe pas.
    ///
    /// Ce qui LIMITE la portée aujourd'hui : il faut être vendeur sur la commande —
    /// donc l'acheteur a acheté aux deux — et le litige n'expose ni coordonnées ni
    /// moyens de paiement. La lecture d'un fil d'arbitrage reste une fuite réelle.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static async Task<bool> IsInvolvedInOrderAsync(ISender sender, Guid sellerId, Guid orderId, CancellationToken ct)
    {
        var shipments = await sender.Send(new ListShipmentsByOrderQuery(orderId), ct);
        return shipments.IsSuccess && shipments.Value.Any(s => s.SellerId == sellerId);
    }
}
