using MediatR;
using System.Security.Claims;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Ordering.Application.Orders.Queries;

namespace Marketplace.Bff.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — commandes. La liste ne renvoie que les commandes du vendeur ; le
/// détail est restreint aux lignes du vendeur (un autre vendeur de la même
/// commande ne doit pas voir ses lignes), avec sous-total recalculé.
/// </summary>
public static class SellerOrderEndpoints
{
    public static IEndpointRouteBuilder MapSellerOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapSellerGroup("/seller/orders", "Vendeur · Commandes");

        orders.MapGet("/", ListMineAsync);
        // AVANT la route « /{id:guid} » n'est pas nécessaire — la contrainte de route
        // exclut déjà « series » —, mais la lisibilité veut que les collections
        // précèdent les éléments.
        orders.MapGet("/series", SeriesAsync);
        orders.MapGet("/{id:guid}", DetailAsync);

        return app;
    }

    /// <summary>
    /// Commandes du vendeur, avec le nom du client (Identity).
    ///
    /// PAGINÉE (<c>?page=&amp;pageSize=</c>) et filtrable (<c>?search=</c> sur la
    /// référence). Sans paramètre, tout est renvoyé — les appelants existants
    /// (dashboard MAUI) continuent donc de fonctionner.
    ///
    /// La pagination a un second effet, moins visible mais décisif : la résolution
    /// du nom de l'acheteur coûte UNE requête Identity par acheteur. Sur 2 000
    /// commandes, c'était 2 000 requêtes à chaque affichage de liste. On ne résout
    /// désormais que les acheteurs de la page demandée.
    /// </summary>
    private static async Task<IResult> ListMineAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, IIdentityModuleApi identity,
        CancellationToken ct, int? page = null, int? pageSize = null, string? search = null)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var result = await sender.Send(new ListOrdersBySellerQuery(seller.Id), ct);
        if (result.IsFailure)
        {
            return result.Match(list => Results.Ok(list));
        }

        var scoped = result.Value
            .Select(o => ScopeToSeller(o, seller.Id))
            // Les plus récentes d'abord : sans tri stable, la pagination renverrait
            // deux fois la même commande sur des pages différentes.
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToList();

        // Recherche par référence (« CMD-A1B2C3D4 » ou juste « a1b2 »). On ne
        // cherche PAS sur le nom du client : cela obligerait à résoudre les 2 000
        // acheteurs avant de filtrer — exactement ce qu'on veut éviter.
        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim().Replace("CMD-", "", StringComparison.OrdinalIgnoreCase);
            scoped = scoped
                .Where(o => o.Id.ToString("N").StartsWith(needle, StringComparison.OrdinalIgnoreCase)
                            || o.Lines.Any(l => l.Sku.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        scoped = SellerBff.Paginate(scoped, page, pageSize);

        // Nom du client résolu une seule fois par acheteur DE LA PAGE.
        var names = new Dictionary<Guid, string>();
        foreach (var buyerId in scoped.Select(o => o.BuyerId).Distinct())
        {
            var u = await identity.GetUserAsync(buyerId, ct);
            var name = u is null ? "" : $"{u.FirstName} {u.LastName}".Trim();
            names[buyerId] = string.IsNullOrWhiteSpace(name) ? "Client" : name;
        }

        var payload = scoped.Select(o => new
        {
            id = o.Id,
            buyerId = o.BuyerId,
            // DEVISE PROJETÉE : `OrderSummary` la portait, la LISTE l'omettait — alors
            // que le DÉTAIL la renvoyait. La console formatait donc les totaux de la
            // liste en franc CFA par défaut, et ceux de la fiche dans la vraie devise :
            // le vendeur cliquait sur une ligne à « 12 F CFA » et arrivait sur une fiche
            // qui disait « 12,40 € ». Même commande, deux montants.
            currency = o.Currency,
            status = o.Status,
            paymentStatus = ToPaymentStatus(o.Status),
            createdAtUtc = o.CreatedAtUtc,
            subtotal = o.Subtotal,
            grandTotal = o.GrandTotal,
            customer = names.TryGetValue(o.BuyerId, out var n) ? n : "Client",
            lines = o.Lines.Select(l => new
            {
                productId = l.ProductId,
                sku = l.Sku,
                quantity = l.Quantity,
                finalUnitPrice = l.FinalUnitPrice,
                lineTotal = l.LineTotal,
            }),
        });

        return Results.Ok(payload);
    }

    /// <summary>
    /// Détail commande restreint aux lignes du vendeur (404 s'il n'est pas concerné),
    /// enrichi pour le dashboard : chaque ligne porte le nom et l'image principale du
    /// produit (via Catalog), la commande porte le nom du client (via Identity) et
    /// l'adresse de livraison figée. Anti-IDOR : on ne projette que les lignes du
    /// vendeur du jeton.
    /// </summary>
    private static async Task<IResult> DetailAsync(
        Guid id, ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers,
        ICatalogModuleApi catalog, IIdentityModuleApi identity, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var order = await sender.Send(new GetOrderQuery(id), ct);
        if (order.IsFailure)
        {
            return order.Match(o => Results.Ok(o));
        }

        var scoped = ScopeToSeller(order.Value, seller.Id);
        if (scoped.Lines.Count == 0)
        {
            return SellerBff.NotFoundOrForbidden("order");
        }

        // Nom du client (résolu via Identity, repli anonymisé sinon).
        var customer = "Client";
        var u = await identity.GetUserAsync(scoped.BuyerId, ct);
        if (u is not null)
        {
            var name = $"{u.FirstName} {u.LastName}".Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                customer = name;
            }
        }

        // Lignes enrichies (séquentiel : le DbContext scoped de Catalog n'est pas thread-safe).
        var lines = new List<object>(scoped.Lines.Count);
        foreach (var l in scoped.Lines)
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
                finalUnitPrice = l.FinalUnitPrice,
                lineTotal = l.LineTotal,
            });
        }

        return Results.Ok(new
        {
            id = scoped.Id,
            buyerId = scoped.BuyerId,
            customer,
            currency = scoped.Currency,
            status = scoped.Status,
            paymentStatus = ToPaymentStatus(scoped.Status),
            createdAtUtc = scoped.CreatedAtUtc,
            subtotal = scoped.Subtotal,
            totalSellerDiscount = scoped.TotalSellerDiscount,
            totalPlatformDiscount = scoped.TotalPlatformDiscount,
            grandTotal = scoped.GrandTotal,
            shippingFee = scoped.ShippingFee,
            shippingAddress = scoped.ShippingAddress is null ? null : new
            {
                label = scoped.ShippingAddress.Label,
                recipient = scoped.ShippingAddress.Recipient,
                phone = scoped.ShippingAddress.Phone,
                communeCode = scoped.ShippingAddress.CommuneCode,
                // Le LIBELLÉ est envoyé résolu : ni l'application ni la console n'ont à
                // embarquer les 77 communes pour afficher une commande déjà passée.
                communeName = scoped.ShippingAddress.CommuneName,
                quartier = scoped.ShippingAddress.Quartier,
                // Le point de repère : ce que le livreur lit en premier.
                landmark = scoped.ShippingAddress.Landmark,
                line1 = scoped.ShippingAddress.Line1,
                countryCode = scoped.ShippingAddress.CountryCode,
                // Position figée : elle alimente le lien « ouvrir dans Maps » que le
                // vendeur transmet au coursier. `null` si l'acheteur n'en avait pas.
                latitude = scoped.ShippingAddress.Latitude,
                longitude = scoped.ShippingAddress.Longitude,
            },
            lines,
        });
    }

    /// <summary>
    /// Statut de PAIEMENT dérivé du statut de commande Ordering (source de vérité :
    /// l'agrégat Order, dont le cycle de vie est piloté par le Saga —
    /// PaymentCaptured → Paid/Confirmed, PaymentFailed → Cancelled). Champ additif,
    /// distinct de <c>status</c> (statut commande/fulfillment).
    /// Valeurs : "Paid" / "Pending" / "Refunded" / "Failed".
    /// </summary>
    private static string ToPaymentStatus(string? orderStatus) => (orderStatus ?? string.Empty).ToLowerInvariant() switch
    {
        // Payé : le paiement est encaissé (Paid) puis la commande est confirmée/livrée.
        "paid" or "confirmed" or "delivered" => "Paid",
        // Remboursé : pas d'état dédié dans Ordering aujourd'hui (au mieux).
        "refunded" => "Refunded",
        // Échec du paiement / du Saga : annulée ou échouée.
        "failed" or "cancelled" or "canceled" => "Failed",
        // Pending / AwaitingPayment (et tout état antérieur) : paiement non encore encaissé.
        _ => "Pending",
    };

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// COMMANDES PAR JOUR, SUR UNE PÉRIODE. POURQUOI CETTE ROUTE EXISTE.
    ///
    /// La liste est PAGINÉE. Une courbe d'évolution construite côté console à partir
    /// de la page affichée n'aurait montré que vingt commandes triées par date, avec
    /// l'allure d'une tendance : un graphe faux est pire qu'une absence de graphe,
    /// parce qu'on le croit.
    ///
    /// L'agrégation se fait donc ici, sur l'ENSEMBLE des commandes du vendeur —
    /// `ListOrdersBySellerQuery` les rend toutes, la pagination n'intervenant que plus
    /// bas dans `ListMineAsync`.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// AUCUN MONTANT ICI, ET C'EST DÉLIBÉRÉ.
    ///
    /// La somme des sous-totaux de commandes N'EST PAS un chiffre d'affaires : elle
    /// ignore les annulations, les remboursements, la commission et les frais du
    /// prestataire. Le relevé (`/seller/finance/statement`) porte déjà les montants
    /// réellement crédités, et son en-tête dit pourquoi il est le seul à faire foi.
    /// Servir ici un second montant, calculé autrement, aurait mis deux chiffres
    /// contradictoires sur deux écrans de la même console.
    ///
    /// Cette route compte des commandes. Les montants se lisent dans le relevé.
    /// ─────────────────────────────────────────────────────────────────────────────
    ///
    /// `offsetMinutes` : décalage du vendeur par rapport à UTC, en minutes vers l'est
    /// (60 pour le Bénin). Les seaux sont des JOURS, et un jour n'existe que dans un
    /// fuseau : sans ce décalage, une commande passée à 00 h 30 à Cotonou tomberait la
    /// VEILLE, parce qu'elle vaut 23 h 30 UTC. La console envoie le décalage réel du
    /// navigateur. Limite assumée : un fuseau à heure d'été verrait les jours situés
    /// de l'autre côté du changement décalés d'une heure — sans objet en UEMOA, qui
    /// n'en pratique pas.
    /// </summary>
    private static async Task<IResult> SeriesAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, CancellationToken ct,
        DateTime? from = null, DateTime? to = null, int offsetMinutes = 0)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        // Bornes du décalage : au-delà de ±14 h aucun fuseau n'existe, et une valeur
        // aberrante déplacerait silencieusement toutes les commandes d'un jour.
        if (offsetMinutes < -840 || offsetMinutes > 840)
        {
            return Results.BadRequest(new
            {
                error = "invalid_offset",
                message = "Décalage horaire hors des fuseaux existants (±14 h).",
            });
        }

        var end = ToUtc(to) ?? DateTime.UtcNow;
        var start = ToUtc(from) ?? end.AddDays(-30);
        if (end <= start)
        {
            return Results.BadRequest(new
            {
                error = "invalid_period",
                message = "La date de fin doit être postérieure à la date de début.",
            });
        }

        // ═════════════════════════════════════════════════════════════════════════
        // LE DÉCALAGE S'APPLIQUE APRÈS AVOIR VÉRIFIÉ QU'IL NE FAIT PAS DÉBORDER.
        //
        // `start + offset` était calculé AVANT le contrôle de période. Aux extrêmes du
        // domaine `DateTime`, l'addition lève `ArgumentOutOfRangeException` :
        // « from=0001-01-01T00:00:00Z&offsetMinutes=-1 » rendait un 500 non géré, avec
        // deux valeurs pourtant acceptées par les bornes que ce handler s'était
        // données. Un contrôle qu'on peut franchir par le bas n'est pas un contrôle.
        // ═════════════════════════════════════════════════════════════════════════
        var offset = TimeSpan.FromMinutes(offsetMinutes);
        if (start.Ticks + offset.Ticks < DateTime.MinValue.Ticks
            || end.Ticks + offset.Ticks > DateTime.MaxValue.Ticks)
        {
            return Results.BadRequest(new
            {
                error = "invalid_period",
                message = "Les dates demandées sortent des valeurs représentables.",
            });
        }

        // Plafond de période : un seau par jour, et on renvoie les jours VIDES (voir
        // plus bas). Au-delà de 400 points, la réponse n'est plus affichable — et la
        // console applique le même plafond à sa courbe de relevé, pour la même raison.
        //
        // ─────────────────────────────────────────────────────────────────────────
        // CE PLAFOND NE PROTÈGE PAS LE SERVEUR, ET IL NE FAUT PAS LE CROIRE.
        //
        // `ListOrdersBySellerQuery` passe par `OrderRepository.ListBySellerAsync`, qui
        // fait un `Include(o => o.Lines)` SANS filtre de date ni `Take` : tout
        // l'historique de commandes du vendeur, lignes comprises, est matérialisé —
        // que la fenêtre demandée fasse sept jours ou quatre cents. Le filtre de
        // période ci-dessous s'applique EN MÉMOIRE, après.
        //
        // C'est la même dépense que `ListMineAsync` consent déjà à chaque affichage de
        // liste, et son propre commentaire prend deux mille commandes pour ordre de
        // grandeur. Borner réellement demande un filtre de date jusque dans le dépôt,
        // c'est-à-dire une signature de plus sur `IOrderRepository` — un chantier de
        // module, pas de BFF. Ce plafond-ci ne borne que la TAILLE DE LA RÉPONSE.
        // ─────────────────────────────────────────────────────────────────────────
        var premierJour = DateOnly.FromDateTime(start + offset);
        var dernierJour = DateOnly.FromDateTime(end + offset);
        var nbJours = dernierJour.DayNumber - premierJour.DayNumber + 1;
        if (nbJours > 400)
        {
            return Results.BadRequest(new
            {
                error = "period_too_long",
                message = "La période ne peut pas dépasser 400 jours.",
            });
        }

        var result = await sender.Send(new ListOrdersBySellerQuery(seller.Id), ct);
        if (result.IsFailure)
        {
            // Même politique que le relevé : on ne sert PAS une courbe à zéro sur une
            // panne. Une courbe plate se lit « vous n'avez rien vendu », pas « la
            // donnée manque », et c'est la confusion la plus coûteuse de cet écran.
            //
            // `Match` rend le ProblemDetails de l'erreur ; la branche de succès n'est
            // jamais empruntée ici, on est déjà dans `IsFailure`.
            return result.Match(_ => Results.Ok(new { }));
        }

        var dansLaPeriode = result.Value
            .Where(o => o.CreatedAtUtc >= start && o.CreatedAtUtc <= end)
            .ToList();

        // ─────────────────────────────────────────────────────────────────────────
        // LES DEUX PRÉDICATS SONT ÉCRITS UNE FOIS, ET C'EST LE POINT.
        //
        // Ils servent à la fois aux points de la courbe et aux totaux de l'écran.
        // Recopier « annulée » d'un côté et de l'autre aurait suffi à ce qu'une
        // vignette annonce 4 annulations pendant que la courbe en trace 3 — le genre
        // d'écart qu'on ne voit qu'une fois en production, et qui fait douter des
        // deux chiffres à la fois.
        //
        // `Failed` COMPTE COMME ANNULÉE, et ce n'est pas une approximation : c'est un
        // paiement refusé, donc une vente qui n'aura pas lieu. Le vendeur n'a rien à
        // en faire, et la distinguer lui demanderait de connaître le vocabulaire du
        // Saga de paiement.
        // ─────────────────────────────────────────────────────────────────────────
        static bool EstAnnulee(string statut)
            => statut.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
               || statut.Equals("Failed", StringComparison.OrdinalIgnoreCase);

        static bool EstLivree(string statut)
            => statut.Equals("Delivered", StringComparison.OrdinalIgnoreCase);

        var comptes = new Dictionary<DateOnly, (int Total, int Annulees)>(nbJours);
        foreach (var o in dansLaPeriode)
        {
            var jour = DateOnly.FromDateTime(o.CreatedAtUtc + offset);
            comptes.TryGetValue(jour, out var c);
            comptes[jour] = (c.Total + 1, c.Annulees + (EstAnnulee(o.Status) ? 1 : 0));
        }

        // ─────────────────────────────────────────────────────────────────────────
        // LES JOURS SANS COMMANDE SONT RENVOYÉS, À ZÉRO.
        //
        // Une courbe qui saute les jours vides relie le 3 au 11 par un segment droit
        // et donne à une semaine morte l'allure d'une progression régulière. C'est au
        // serveur de rendre la grille complète : la console ne peut pas deviner les
        // jours qu'elle n'a pas reçus.
        // ─────────────────────────────────────────────────────────────────────────
        var points = new List<object>(nbJours);
        for (var i = 0; i < nbJours; i++)
        {
            var jour = premierJour.AddDays(i);
            comptes.TryGetValue(jour, out var c);
            points.Add(new
            {
                date = jour.ToString("yyyy-MM-dd"),
                orders = c.Total,
                cancelled = c.Annulees,
            });
        }

        // ─────────────────────────────────────────────────────────────────────────
        // LES TROIS TRANCHES PARTITIONNENT LE TOTAL, PAR CONSTRUCTION.
        //
        // `annulées` et `livrées` se comptent sur un statut nommé ; « en cours » est
        // le RESTE — jamais une troisième liste de statuts. Deux raisons, et la
        // seconde est la vraie :
        //
        //   1. Les quatre chiffres s'additionnent toujours. Un vendeur qui vérifie
        //      « 12 = 3 + 7 + 2 » trouve son compte, et c'est le premier geste de
        //      quiconque découvre un tableau de bord.
        //   2. Un statut AJOUTÉ demain au domaine (« Returned », « OnHold »…) tombe
        //      automatiquement dans « en cours » au lieu de disparaître de l'écran.
        //      Avec trois listes explicites, il serait compté dans le total et dans
        //      aucune tranche : les vignettes cesseraient de s'additionner sans que
        //      rien, nulle part, ne signale l'écart.
        //
        // « EN COURS » N'EST PAS « À TRAITER », et les deux écrans ne mentent pas
        // pour autant. Le reste inclut `Pending` et `AwaitingPayment` — des commandes
        // dont le paiement n'est pas encaissé, sur lesquelles le vendeur ne peut RIEN
        // faire. Elles sont bien en cours, elles ne sont pas dans sa file de travail.
        // Le compteur « À traiter » du tableau de bord, lui, se limite à `Paid` et
        // `Confirmed` : c'est une autre question, posée sur un autre écran.
        // ─────────────────────────────────────────────────────────────────────────
        var annulees = dansLaPeriode.Count(o => EstAnnulee(o.Status));
        var livrees = dansLaPeriode.Count(o => EstLivree(o.Status));

        return Results.Ok(new
        {
            from = start,
            to = end,
            offsetMinutes,
            totalOrders = dansLaPeriode.Count,
            deliveredOrders = livrees,
            cancelledOrders = annulees,
            inProgressOrders = dansLaPeriode.Count - livrees - annulees,
            points,
        });
    }

    /// <summary>Normalise une date en UTC (les colonnes sont « timestamptz », Npgsql exige Kind=Utc).</summary>
    private static DateTime? ToUtc(DateTime? value)
        => value is null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

    /// <summary>Projette une commande sur les seules lignes du vendeur + totaux recalculés.</summary>
    private static OrderSummary ScopeToSeller(OrderSummary order, Guid sellerId)
    {
        var lines = order.Lines.Where(l => l.SellerId == sellerId).ToList();
        var subtotal = lines.Sum(l => l.LineTotal);
        return order with
        {
            Lines = lines,
            Subtotal = subtotal,
            // Montants de LIGNE : plus de multiplication par la quantité.
            TotalSellerDiscount = lines.Sum(l => l.SellerDiscount),
            TotalPlatformDiscount = lines.Sum(l => l.PlatformDiscount),
            GrandTotal = subtotal
        };
    }
}
