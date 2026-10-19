using MediatR;
using System.Linq;
using System.Security.Claims;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Ordering.Application.Orders.Commands;
using Marketplace.Modules.Ordering.Application.Orders.Queries;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Returns.Application.Returns;
using Marketplace.Modules.Shipping.Contracts;

namespace Marketplace.Host.Client.Endpoints;

/// <summary>
/// BFF Client — commandes. Le détail agrège la commande et ses expéditions
/// (tracking) en un appel.
/// </summary>
public static class ClientOrderEndpoints
{
    public static IEndpointRouteBuilder MapClientOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/client/orders").WithTags("Client · Commandes").RequireAuthorization();

        orders.MapGet("/", ListAsync);
        orders.MapGet("/{id:guid}", DetailAsync);
        orders.MapPost("/{id:guid}/cancel", CancelAsync);
        orders.MapPost("/{id:guid}/return", ReturnAsync);
        orders.MapGet("/{id:guid}/tracking", TrackingAsync);

        // LE CODE DE REMISE — ROUTE SÉPARÉE, ET RÉSERVÉE À L'ACHETEUR.
        //
        // Elle ne rejoint pas `/tracking` : ce suivi rend des `ShipmentSummary`, le même
        // type que le BFF vendeur sert au vendeur. Un secret ne voyage pas dans un
        // wagon partagé.
        orders.MapGet("/{id:guid}/delivery-codes", DeliveryCodesAsync).WithName("ClientOrderDeliveryCodes");

        // ═══════════════════════════════════════════════════════════════════════════
        // LE SUIVI VIVANT, À CÔTÉ DU SUIVI D'EXPÉDITION — ET NON DEDANS.
        //
        // `/tracking` rend les expéditions : leur statut, leur transporteur, leur numéro.
        // Ce sont des faits ENREGISTRÉS, qui bougent quelques fois par commande, et
        // l'écran les demande une fois.
        //
        // Celle-ci rend une position, qui bouge toutes les quinze secondes et que l'écran
        // redemande au même rythme tant que la livraison dure. Les fondre en une route
        // ferait repasser par la base la commande, ses expéditions et leurs lignes à
        // chaque sondage — pour un couple de coordonnées.
        //
        // ET SURTOUT : L'UNE DÉPEND D'UN AUTRE SERVICE, PAS L'AUTRE. Un dispatch
        // injoignable rendrait le détail de commande indisponible s'il était dans le
        // même appel. Séparées, la page s'affiche entière et seule la carte dit qu'elle
        // ne sait pas.
        // ═══════════════════════════════════════════════════════════════════════════
        orders.MapGet("/{id:guid}/invoice", InvoiceAsync);

        return app;
    }

    /// <summary>
    /// Liste des commandes de l'utilisateur.
    /// </summary>
    private static async Task<IResult> ListAsync(ClaimsPrincipal user, ISender sender, CancellationToken ct)
        => ClientBff.CurrentUserId(user) is not { } buyerId
            ? Results.Unauthorized()
            : (await sender.Send(new ListMyOrdersQuery(buyerId), ct)).Match(o => Results.Ok(o));

    /// <summary>
    /// Détail commande + expéditions + tracking (1 appel), avec chaque ligne
    /// enrichie du nom et de l'image principale du produit (le résumé commande
    /// ne porte que le SKU).
    /// </summary>
    private static async Task<IResult> DetailAsync(ClaimsPrincipal user, Guid id, ISender sender, IShippingModuleApi shipping, ICatalogModuleApi catalog, CancellationToken ct)
    {
        // Contrôle de propriété : la commande doit appartenir à l'acheteur du jeton.
        var o = await LoadOwnedOrderAsync(user, id, sender, ct);
        if (o is null)
        {
            return Results.NotFound();
        }

        // Séquentiel : le DbContext (scoped) de Catalog n'est pas thread-safe.
        var lines = new List<object>(o.Lines.Count);
        foreach (var l in o.Lines)
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
                // Vendeur du produit : permet à l'app d'ouvrir la conversation avec LE
                // vendeur de cette commande (bouton « Vendeur » du détail commande).
                sellerId = product?.SellerId,
                sku = l.Sku,
                productName = name ?? l.Sku,
                imageUrl,
                quantity = l.Quantity,
                finalUnitPrice = l.FinalUnitPrice,
                lineTotal = l.LineTotal,
            });
        }

        var shipments = await shipping.GetShipmentsByOrderAsync(id, ct);

        return Results.Ok(new
        {
            order = new
            {
                id = o.Id,
                buyerId = o.BuyerId,
                currency = o.Currency,
                status = o.Status,
                createdAtUtc = o.CreatedAtUtc,
                subtotal = o.Subtotal,
                totalSellerDiscount = o.TotalSellerDiscount,
                totalPlatformDiscount = o.TotalPlatformDiscount,
                // Frais de livraison figés à la commande. Ils manquaient dans cette
                // réponse alors que le domaine les porte : l'application affichait donc
                // le total sous le libellé « Sous-total », et l'acheteur voyait un article
                // à 1 150 XOF suivi d'un « sous-total » de 2 650 sans explication. Les
                // 1 500 manquants étaient la livraison, simplement jamais envoyée.
                shippingFee = o.ShippingFee,
                // ─────────────────────────────────────────────────────────────────
                // LE FRAIS DE SERVICE, ENVOYÉ AVEC SON TAUX ET SON ASSIETTE.
                //
                // Même leçon que les frais de livraison juste au-dessus : un montant
                // que le domaine porte mais que la réponse tait devient un écart
                // inexpliqué à l'écran. `itemsTotal` est envoyé PLUTÔT QUE laissé à
                // calculer côté client — trois soustractions faites dans deux
                // applications différentes finiraient par diverger d'un franc.
                // ─────────────────────────────────────────────────────────────────
                itemsTotal = o.ItemsTotal,
                platformFee = o.PlatformFee,
                platformFeeRate = o.PlatformFeeRate,
                grandTotal = o.GrandTotal,
                shippingAddress = o.ShippingAddress is null ? null : new
                {
                    label = o.ShippingAddress.Label,
                    recipient = o.ShippingAddress.Recipient,
                    phone = o.ShippingAddress.Phone,
                    communeCode = o.ShippingAddress.CommuneCode,
                    // Le LIBELLÉ est envoyé résolu : ni l'application ni la console n'ont à
                    // embarquer les 77 communes pour afficher une commande déjà passée.
                    communeName = o.ShippingAddress.CommuneName,
                    quartier = o.ShippingAddress.Quartier,
                    // Le point de repère : ce que le livreur lit en premier.
                    landmark = o.ShippingAddress.Landmark,
                    line1 = o.ShippingAddress.Line1,
                    countryCode = o.ShippingAddress.CountryCode,
                    // Position figée : elle alimente le lien « ouvrir dans Maps » que le
                    // vendeur transmet au coursier. `null` si l'acheteur n'en avait pas.
                    latitude = o.ShippingAddress.Latitude,
                    longitude = o.ShippingAddress.Longitude,
                },
                lines,
            },
            shipments,
        });
    }

    /// <summary>Demande d'annulation (si éligible). Réservée au propriétaire de la commande.</summary>
    private static async Task<IResult> CancelAsync(ClaimsPrincipal user, Guid id, CancelRequest? request, ISender sender, CancellationToken ct)
    {
        if (await LoadOwnedOrderAsync(user, id, sender, ct) is null)
        {
            return Results.NotFound();
        }
        return (await sender.Send(new CancelOrderCommand(id, request?.Reason ?? string.Empty), ct)).Match(() => Results.NoContent());
    }

    /// <summary>
    /// Ouvre une demande de retour (RMA) sur une ligne de la commande.
    ///
    /// C'ÉTAIT LA SEULE ROUTE `/{id}` DE CE FICHIER À NE PAS CHARGER LA COMMANDE
    /// POSSÉDÉE. `CancelAsync` juste au-dessus et `TrackingAsync` juste en dessous
    /// appellent `LoadOwnedOrderAsync` ; celle-ci passait l'identifiant tel quel et
    /// laissait le domaine trancher — lequel répondait 403, donc confirmait l'existence.
    ///
    /// Le domaine a été corrigé aussi (voir RequestReturnCommandHandler) : c'est lui qui
    /// protège l'autre hôte. La garde est reposée ici pour que le fichier redevienne
    /// homogène — quatre routes par identifiant, quatre fois le même geste. Une exception
    /// silencieuse au milieu d'un motif régulier est ce qui a permis à celle-ci de
    /// passer inaperçue.
    /// </summary>
    private static async Task<IResult> ReturnAsync(ClaimsPrincipal user, Guid id, ReturnRequest request, ISender sender, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        if (await LoadOwnedOrderAsync(user, id, sender, ct) is null)
        {
            return Results.NotFound();
        }

        return (await sender.Send(new RequestReturnCommand(buyerId, id, request.OfferId, request.Reason), ct))
            .Match(rid => Results.Ok(new { returnRequestId = rid }));
    }

    /// <summary>
    /// Les codes que l'acheteur donnera aux vendeurs à la remise. Réservé au propriétaire.
    ///
    /// DOUBLE CONTRÔLE, DÉLIBÉRÉMENT : `LoadOwnedOrderAsync` vérifie que la commande est
    /// bien la sienne, puis la requête revérifie l'acheteur expédition par expédition.
    /// Un seul verrou sur un secret, c'est un verrou de trop peu.
    /// </summary>
    private static async Task<IResult> DeliveryCodesAsync(
        ClaimsPrincipal user, Guid id, ISender sender, IShippingModuleApi shipping, CancellationToken ct)
    {
        if (await LoadOwnedOrderAsync(user, id, sender, ct) is null)
        {
            return Results.NotFound();
        }

        if (ClientBff.CurrentUserId(user) is not { } buyerId)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await shipping.GetDeliveryCodesByOrderAsync(id, buyerId, ct));
    }

    /// <summary>Suivi d'expédition (expéditions de la commande + statuts). Réservé au propriétaire.</summary>
    private static async Task<IResult> TrackingAsync(ClaimsPrincipal user, Guid id, ISender sender, IShippingModuleApi shipping, CancellationToken ct)
    {
        if (await LoadOwnedOrderAsync(user, id, sender, ct) is null)
        {
            return Results.NotFound();
        }
        return Results.Ok(await shipping.GetShipmentsByOrderAsync(id, ct));
    }


    /// <summary>
    /// Charge une commande en garantissant qu'elle appartient à l'acheteur du jeton.
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// FACTURE D'UNE COMMANDE, POUR L'ACHETEUR.
    ///
    /// À ne pas confondre avec `Billing.Invoice`, qui est une facture VENDEUR : un
    /// relevé de commissions sur une période. Celle-ci est le justificatif d'UN achat,
    /// destiné à la personne qui a payé.
    ///
    /// ELLE N'EST PAS STOCKÉE, ELLE EST RENDUE. Tout ce qu'elle contient est déjà figé
    /// sur la commande — les prix, les remises, la livraison, le taux de TVA, le taux
    /// du frais de service. Créer une table de factures aurait dupliqué ces montants
    /// dans une seconde source de vérité, avec la garantie qu'un jour les deux
    /// divergent ; et le vrai besoin d'une facture — être IDENTIQUE à chaque
    /// réimpression — est déjà couvert par le gel des taux sur la commande.
    ///
    /// LA TVA EST COMPRISE, LE FRAIS DE SERVICE EST AJOUTÉ. Les deux montants
    /// apparaissent, mais pas au même endroit du décompte : `taxTotal` est une part de
    /// `total`, `platformFee` s'y ajoute. Les présenter côte à côte sans le dire
    /// conduirait n'importe quel comptable à additionner ce qui ne s'additionne pas.
    ///
    /// LE NUMÉRO EST DÉRIVÉ, PAS TIRÉ D'UNE SÉQUENCE. La commande n'a pas de référence
    /// lisible, et en inventer une ici demanderait un compteur — donc un état, donc
    /// des trous à la moindre transaction annulée. Les huit premiers caractères de
    /// l'identifiant, préfixés de l'année, donnent un numéro stable, unique en
    /// pratique, et reproductible : la même commande rend toujours la même facture.
    /// Le jour où une numérotation séquentielle sera exigée par le fisc béninois,
    /// elle devra vivre dans le domaine, pas ici.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> InvoiceAsync(
        ClaimsPrincipal user, Guid id, ISender sender, ICatalogModuleApi catalog, CancellationToken ct)
    {
        var o = await LoadOwnedOrderAsync(user, id, sender, ct);
        if (o is null)
        {
            return Results.NotFound();
        }

        // Une commande sans paiement abouti n'est pas une facture : c'est un devis, ou
        // une tentative. Éditer un justificatif d'achat pour une commande annulée ou en
        // attente donnerait à l'acheteur une preuve de paiement qu'il n'a pas.
        if (!EstFacturable(o.Status))
        {
            return Results.Conflict(new
            {
                error = "ordering.not_invoiceable",
                message = $"La commande est « {o.Status} » : une facture n'est émise qu'une fois le paiement encaissé.",
            });
        }

        // Séquentiel : le DbContext (scoped) de Catalog n'est pas thread-safe. Même
        // raison qu'au détail de commande juste au-dessus.
        var lignes = new List<object>(o.Lines.Count);
        foreach (var l in o.Lines)
        {
            var produit = await catalog.GetProductAsync(l.ProductId, ct);
            lignes.Add(new
            {
                designation = produit?.Name ?? l.Sku,
                sku = l.Sku,
                quantite = l.Quantity,
                prixUnitaire = l.FinalUnitPrice,
                remise = l.SellerDiscount + l.PlatformDiscount,
                total = l.LineTotal,
            });
        }

        return Results.Ok(new
        {
            numero = $"HBA-{o.CreatedAtUtc:yyyy}-{o.Id.ToString("N")[..8].ToUpperInvariant()}",
            emiseLe = DateTime.UtcNow,
            commande = new { id = o.Id, passeeLe = o.CreatedAtUtc, statut = o.Status },
            devise = o.Currency,
            client = o.ShippingAddress is null ? null : new
            {
                nom = o.ShippingAddress.Recipient,
                telephone = o.ShippingAddress.Phone,
                adresse = o.ShippingAddress.Line1,
                quartier = o.ShippingAddress.Quartier,
                commune = o.ShippingAddress.CommuneName,
                pays = o.ShippingAddress.CountryCode,
            },
            lignes,
            totaux = new
            {
                // L'ordre de ces champs EST le décompte, de haut en bas.
                sousTotal = o.Subtotal,
                remiseVendeur = o.TotalSellerDiscount,
                remisePlateforme = o.TotalPlatformDiscount,
                articles = o.ItemsTotal,
                livraison = o.ShippingFee,
                fraisDeService = o.PlatformFee,
                tauxFraisDeService = o.PlatformFeeRate,
                total = o.GrandTotal,
                // Réglé par la cagnotte, et ce qui a donc été encaissé par un moyen
                // de paiement. Les deux doivent apparaître : un acheteur qui a payé
                // moitié cagnotte cherchera la trace de l'autre moitié.
                cagnotte = o.WalletApplied,
                encaisse = o.AmountDue,
            },
            tva = new
            {
                // COMPRISE dans `total`, jamais ajoutée : régime TVA incluse.
                comprise = true,
                montant = o.TaxTotal,
                taux = o.TaxRate,
                horsTaxe = o.NetTotal,
            },
        });
    }

    /// <summary>
    /// Statuts pour lesquels une facture a un sens : le paiement est encaissé.
    ///
    /// LA LISTE EST CELLE DE `OrderStatus`, PAS UNE LISTE PLAUSIBLE. L'énumération du
    /// domaine ne connaît que Pending, AwaitingPayment, Paid, Confirmed, Cancelled,
    /// Failed et Delivered — pas de « Shipped » ni de « Completed ». Un statut inventé
    /// ici ne casserait rien à la compilation : il rendrait simplement la facture
    /// introuvable pour les commandes concernées, sans un message d'erreur pour le
    /// dire.
    ///
    /// `Cancelled` et `Failed` en sont exclus : ils n'ont rien encaissé. Une commande
    /// remboursée garde en revanche sa facture — c'est l'AVOIR qui manque, et ce sera
    /// un autre document.
    /// </summary>
    private static bool EstFacturable(string statut)
        => statut is "Paid" or "Confirmed" or "Delivered";

    /// <summary>
    /// Renvoie null si la commande est absente OU ne lui appartient pas (on ne
    /// distingue pas les deux cas : pas de divulgation d'existence, anti-IDOR).
    /// </summary>
    private static async Task<OrderSummary?> LoadOwnedOrderAsync(ClaimsPrincipal user, Guid id, ISender sender, CancellationToken ct)
    {
        if (ClientBff.CurrentUserId(user) is not { } buyerId)
        {
            return null;
        }
        var result = await sender.Send(new GetOrderQuery(id), ct);
        if (result.IsFailure)
        {
            return null;
        }
        var order = result.Value;
        return order.BuyerId == buyerId ? order : null;
    }

    public sealed record ClientOrderDetail(OrderSummary Order, IReadOnlyList<ShipmentSummary> Shipments);
    public sealed record CancelRequest(string? Reason);
    public sealed record ReturnRequest(Guid OfferId, string Reason);
}
