using MediatR;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Analytics.Application.Metrics;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Ordering.Application.Orders.Queries;
using Marketplace.Modules.Settlement.Application.Batches;
using Marketplace.Modules.Reviews.Application.Reviews.Queries;

namespace Marketplace.Host.Seller.Endpoints;

/// <summary>
/// BFF Vendeur — tableau de bord. Agrège en un appel les indicateurs clés de la
/// boutique (commandes, chiffre d'affaires sur 30 j, avis), pour l'écran d'accueil.
/// </summary>
public static class SellerDashboardEndpoints
{
    public static IEndpointRouteBuilder MapSellerDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        // Route isolée (pas de groupe) : le rôle est exigé ici directement, sinon
        // elle passait à travers le verrouillage posé sur les 17 autres groupes.
        app.MapGet("/api/sellers/dashboard", DashboardAsync)
            .WithTags("Vendeur · Tableau de bord")
            .RequireAuthorization(p => p.RequireRole(SellerBff.SellerRole));

        // Statistiques avancées : tendance des ventes (série journalière ou mensuelle).
        app.MapGet("/api/sellers/analytics", AnalyticsAsync)
            .WithTags("Vendeur · Tableau de bord")
            .RequireAuthorization(p => p.RequireRole(SellerBff.SellerRole));

        return app;
    }

    /// <summary>
    /// Tendance des ventes du vendeur sur une plage : série de périodes (commandes,
    /// articles vendus, GMV) + totaux. Réutilise le rapport agrégé du module Analytics.
    /// </summary>
    private static async Task<IResult> AnalyticsAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers, string? granularity, int? days, CancellationToken ct)
    {
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var now = DateTime.UtcNow;
        var isMonth = string.Equals(granularity, "month", StringComparison.OrdinalIgnoreCase);

        string fromKey, toKey, gran;
        if (isMonth)
        {
            var months = days is > 0 and <= 24 ? days.Value : 12;
            fromKey = now.AddMonths(-(months - 1)).ToString("yyyy-MM");
            toKey = now.ToString("yyyy-MM");
            gran = "Month";
        }
        else
        {
            var d = days is > 0 and <= 180 ? days.Value : 30;
            fromKey = now.AddDays(-(d - 1)).ToString("yyyy-MM-dd");
            toKey = now.ToString("yyyy-MM-dd");
            gran = "Day";
        }

        var result = await sender.Send(new GetReportQuery("Seller", seller.Id, gran, fromKey, toKey), ct);
        return result.Match(r => Results.Ok(r));
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LE DISPOSITIF « DONNÉE INDISPONIBLE » ÉTAIT ENTIÈREMENT MORT.
    ///
    /// Ce handler teste `IsFailure` sur trois requêtes, et l'application vendeur a tout
    /// l'affichage correspondant : bandeau, pastille « Indisponible », commentaire de
    /// type qui qualifie `unavailable` de champ le plus important de cet objet. Or les
    /// trois handlers renvoient un `Result.Success` INCONDITIONNEL
    /// (OrderQueries, SettlementQueries, ReviewQueries) : aucun ne peut échouer.
    ///
    /// Une base réellement indisponible ne produit pas un `Result` en échec, elle lève.
    /// Aucun comportement de pipeline ne convertit l'exception (il n'y a que
    /// journalisation et validation), donc elle remontait jusqu'à l'hôte : 500 sur
    /// `/seller/dashboard`, écran entier en erreur, zéro chiffre affiché. C'est
    /// exactement ce que l'objectif écrit plus bas voulait éviter — « perdre les
    /// commandes parce que les avis sont indisponibles serait disproportionné ».
    ///
    /// ON CAPTURE DONC L'EXCEPTION, SECTION PAR SECTION. Attraper large est d'ordinaire
    /// un mauvais réflexe ; ici c'est le mécanisme lui-même : la page est composite, et
    /// la seule alternative est de tout perdre pour un tiers en panne. Les deux garde-
    /// fous qui rendent cela acceptable : l'annulation (`OperationCanceledException`)
    /// n'est PAS un échec de source et doit continuer de remonter — sinon un client qui
    /// raccroche produit un tableau de bord à zéro, journalisé comme une panne — et
    /// chaque capture est journalisée en erreur, avec la section concernée.
    ///
    /// CE FILET ATTRAPE AUSSI LES BOGUES, ET IL FAUT LE SAVOIR. Une référence nulle dans
    /// un mappeur, une erreur de configuration : tout devient « section indisponible »,
    /// et la page répond 200. Le vendeur lit « Indisponible » là où il y a un défaut de
    /// code. C'est le prix du mécanisme — l'alternative est de tout perdre pour un tiers
    /// en panne — et c'est pourquoi la journalisation en ERREUR n'est pas facultative :
    /// elle est la seule chose qui distingue les deux cas.
    ///
    /// UN TROISIÈME « Indisponible » SUR LA MÊME SECTION, PLUSIEURS JOURS DE SUITE, SE
    /// LIT DANS LES JOURNAUX, PAS À L'ÉCRAN.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> DashboardAsync(
        ClaimsPrincipal user, ISender sender, ISellerModuleApi sellers,
        ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Marketplace.Host.Seller.Dashboard");
        var seller = await SellerBff.ResolveSellerAsync(user, sellers, ct);
        if (seller is null)
        {
            return SellerBff.NotASeller();
        }

        var now = DateTime.UtcNow;

        // ─────────────────────────────────────────────────────────────────────────
        // UN ZÉRO PEUT VOULOIR DIRE « RIEN » OU « JE NE SAIS PAS ».
        //
        // Les trois blocs de ce tableau de bord retombaient silencieusement à zéro en
        // cas de panne : zéro commande, zéro chiffre d'affaires, zéro avis. Pour un
        // vendeur, un chiffre d'affaires nul un lundi matin est indiscernable d'une
        // panne — et c'est le genre de zéro qui déclenche un appel au support, voire
        // une inquiétude légitime sur son activité.
        //
        // Contrairement au relevé financier, on ne refuse pas de servir la page : un
        // tableau de bord est composite, et perdre les commandes parce que les avis
        // sont indisponibles serait disproportionné. On TRANSMET donc l'incertitude,
        // via une liste des sections indisponibles.
        //
        // Champ AJOUTÉ, jamais retiré : les clients actuels l'ignorent, une version
        // ultérieure de l'app vendeur pourra afficher « indisponible » à la place du
        // zéro. Changer les champs en nullable aurait cassé le contrat existant.
        // ─────────────────────────────────────────────────────────────────────────
        var unavailable = new List<string>();

        // Commandes du vendeur (toutes + à traiter).
        IReadOnlyList<Marketplace.Modules.Ordering.Contracts.OrderSummary> orders =
            Array.Empty<Marketplace.Modules.Ordering.Contracts.OrderSummary>();
        try
        {
            var ordersResult = await sender.Send(new ListOrdersBySellerQuery(seller.Id), ct);
            if (ordersResult.IsFailure)
            {
                unavailable.Add("orders");
            }
            else
            {
                orders = ordersResult.Value;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Tableau de bord : commandes du vendeur {SellerId} illisibles.", seller.Id);
            unavailable.Add("orders");
        }
        var ordersTotal = orders.Count;
        // ─────────────────────────────────────────────────────────────────────────
        // « À TRAITER » COMPTAIT DES COMMANDES QUE LE VENDEUR NE PEUT PAS TRAITER.
        //
        // Le filtre était « tout sauf livrée ou annulée ». Il retenait donc aussi les
        // trois états d'avant l'encaissement : `Pending` (commande créée, paiement pas
        // même lancé), `AwaitingPayment` (paiement en cours chez le prestataire) et
        // `Failed` (paiement refusé). Dans aucun des trois le vendeur n'a quoi que ce
        // soit à faire — et il ne le POURRAIT pas : les expéditions ne sont créées qu'à
        // la confirmation (CreateShipmentsOnOrderConfirmedHandler), donc sa file
        // d'exécution est vide. Un paiement abandonné laissait ainsi un « à traiter »
        // définitif, que rien ne faisait jamais redescendre.
        //
        // CE FILTRE NE FERME PAS TOUS LES BLOCAGES, ET IL NE FAUT PAS LE CROIRE. Une
        // commande `Confirmed` dont une expédition a été ANNULÉE reste comptée à vie :
        // `Order.Cancel` refuse l'état `Confirmed` (Order.cs:451) et
        // MarkOrderDeliveredOnAllShipmentsDeliveredHandler exige que TOUTES les
        // expéditions soient livrées. La commande n'a donc plus aucune sortie. C'est un
        // trou de la machine à états, pas de ce compteur — et le corriger ici le
        // masquerait au lieu de le fermer.
        //
        // ON GARDE `Paid` EN PLUS DE `Confirmed`. La confirmation suit l'encaissement de
        // très près, mais elle le suit : s'en tenir à `Confirmed` ferait disparaître la
        // commande du compteur pendant cet intervalle, et une vente qui clignote hors du
        // tableau de bord inquiète autant qu'un compteur qui ne bouge pas.
        //
        // CE COMPTEUR RESTE UN COMPTE DE COMMANDES, PAS DE COLIS RESTANTS. Sur une
        // commande partagée avec un autre vendeur, le statut ne passe « livrée » que
        // lorsque TOUTES les expéditions le sont
        // (MarkOrderDeliveredOnAllShipmentsDeliveredHandler) : une commande dont ce
        // vendeur a fini sa part y figure encore. Le décompte du travail réellement
        // restant est l'onglet « À préparer » de la file d'exécution.
        // ─────────────────────────────────────────────────────────────────────────
        var ordersToProcess = orders.Count(o => o.Status is "Paid" or "Confirmed");

        // Répartition par statut : la liste est déjà chargée, l'agrégation est
        // gratuite. Alimente le donut « commandes par statut » côté app.
        var ordersByStatus = orders
            .GroupBy(o => o.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        // Relevé financier sur 30 jours (brut, commissions, net).
        decimal grossSales = 0, netPayout = 0;
        string currency = "XOF";
        try
        {
            var statement = await sender.Send(new GetSellerStatementQuery(seller.Id, now.AddDays(-30), now), ct);
            if (statement.IsSuccess)
            {
                grossSales = statement.Value.GrossSales;
                netPayout = statement.Value.NetPayout;
                currency = statement.Value.Currency;
            }
            else
            {
                unavailable.Add("statement");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Tableau de bord : relevé du vendeur {SellerId} illisible.", seller.Id);
            unavailable.Add("statement");
        }

        // Avis (nombre + note moyenne).
        IReadOnlyList<Marketplace.Modules.Reviews.Contracts.ReviewSummary> reviews =
            Array.Empty<Marketplace.Modules.Reviews.Contracts.ReviewSummary>();
        try
        {
            var reviewsResult = await sender.Send(new ListReviewsBySellerQuery(seller.Id), ct);
            if (reviewsResult.IsFailure)
            {
                unavailable.Add("reviews");
            }
            else
            {
                reviews = reviewsResult.Value;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Tableau de bord : avis du vendeur {SellerId} illisibles.", seller.Id);
            unavailable.Add("reviews");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // LA NOTE DU TABLEAU DE BORD N'ÉTAIT PAS LA NOTE PUBLIQUE.
        //
        // `ListReviewsBySellerQuery` ne filtre PAS la visibilité (ReviewRepository :
        // `ListBySellerAsync` prend tout, là où `PubliclyVisible` retient Published et
        // Flagged). La moyenne affichée en tête de tableau de bord intégrait donc les
        // avis REJETÉS par la modération — que l'acheteur, lui, ne voit jamais.
        //
        // Un vendeur qui se fait retirer deux avis à une étoile voyait sa note bouger
        // sur l'écran Avis et pas ici, ou l'inverse. L'écran Avis, lui, avertit
        // honnêtement ; le tableau de bord, qu'on regarde en premier, affirmait.
        //
        // MÊME LISTE BLANCHE QUE LE DÉPÔT, et pour la même raison : un statut ajouté
        // demain reste hors de la moyenne par défaut, ce qui est le sens le moins
        // dangereux. On garde la liste complète pour le NOMBRE d'avis reçus — le
        // vendeur a bien reçu les avis rejetés, et l'écran Avis les lui montre.
        // ─────────────────────────────────────────────────────────────────────────
        var visibles = reviews
            .Where(r => string.Equals(r.Status, "Published", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(r.Status, "Flagged", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var reviewsCount = reviews.Count;
        var averageRating = visibles.Count > 0 ? Math.Round(visibles.Average(r => r.Rating), 2) : 0d;

        return Results.Ok(new SellerDashboard(
            ordersTotal, ordersToProcess, grossSales, netPayout, currency, reviewsCount, averageRating,
            ordersByStatus, unavailable));
    }

    public sealed record SellerDashboard(
        int OrdersTotal,
        int OrdersToProcess,
        decimal GrossSales30d,
        decimal NetPayout30d,
        string Currency,
        int ReviewsCount,
        double AverageRating,
        IReadOnlyDictionary<string, int> OrdersByStatus,
        /// <summary>
        /// Sections dont les données n'ont PAS pu être récupérées : « orders »,
        /// « statement », « reviews ». Vide en fonctionnement normal.
        ///
        /// Les chiffres correspondants valent alors zéro par défaut — c'est
        /// précisément ce que cette liste permet de ne plus confondre avec une
        /// absence réelle d'activité.
        /// </summary>
        IReadOnlyList<string> Unavailable);
}
