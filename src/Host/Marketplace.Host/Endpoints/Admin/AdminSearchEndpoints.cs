using MediatR;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Search.Application.Maintenance;

namespace Marketplace.Host.Admin.Endpoints;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// BFF ADMIN — RECONSTRUCTION DE L'INDEX DE RECHERCHE.
///
/// L'index de recherche est un read model : il n'a pas de durée de vie, et rien ne le
/// répare tout seul. Il n'existait pourtant AUCUN moyen de le reconstruire — ni
/// commande, ni route, ni service de fond. Une divergence, quelle qu'en soit
/// l'origine, était donc définitive : un renommage de produit d'avant l'existence de
/// l'événement de mise à jour, un message parti en lettre morte, un
/// `OUTBOX_ENABLED=false` posé par erreur le temps d'un déploiement.
///
/// POURQUOI UNE ROUTE ET PAS UN SERVICE DE FOND.
///
/// Une reconstruction lit tout le catalogue et réécrit tout l'index. Ce n'est pas une
/// opération à programmer à l'heure : c'est un geste de réparation, décidé par
/// quelqu'un qui a constaté une divergence, et dont on veut savoir QUAND il a eu lieu.
///
/// SYNCHRONE, ET ASSUMÉ COMME TEL. La réponse porte le compte rendu — produits
/// parcourus, documents écrits, documents visibles — parce qu'une reconstruction dont
/// on ne sait pas si elle a fait son travail ne rassure personne. Sur un très gros
/// catalogue, l'appel est long : c'est le prix d'une réponse qui dit la vérité, et
/// l'administrateur peut le relancer sans dommage, l'opération étant idempotente.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class AdminSearchEndpoints
{
    public static IEndpointRouteBuilder MapAdminSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var search = app.MapAdminGroup("/api/admin/search", "Admin · Recherche");
        search.MapPost("/reindex", RebuildAsync);
        return app;
    }

    /// <summary>
    /// Reconstruit l'index depuis le catalogue. Ne touche ni au prix ni à la note
    /// (Offers et Reviews en sont propriétaires) et ne supprime aucun document.
    /// </summary>
    private static async Task<IResult> RebuildAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new RebuildSearchIndexCommand(), ct)).Match(Results.Ok);
}
