using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Search.Application.Indexing;

namespace Marketplace.Modules.Search.Application.Maintenance;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// RECONSTRUCTION DE L'INDEX DE RECHERCHE, DEPUIS LE CATALOGUE.
///
/// L'index n'avait AUCUN moyen d'être reconstruit : ni commande, ni route, ni service
/// de fond. Toute divergence était donc définitive. Et le rejeu unitaire d'une lettre
/// morte, seul outil de rattrapage existant, ne sert à rien quand l'événement n'a
/// jamais été produit — ce qui était le cas des renommages de produit et des mises en
/// pause d'offre.
///
/// C'est le filet qui rattrape les défauts qu'on n'a pas trouvés.
///
/// CE QU'ELLE RECONSTRUIT, ET CE QU'ELLE NE RECONSTRUIT PAS.
///
/// Le catalogue est la source de vérité de l'identité d'un produit : nom, slug,
/// vendeur, catégorie, image principale, visibilité. Ces champs sont réécrits.
///
/// Le PRIX et la NOTE ne le sont pas : ils appartiennent à Offers et à Reviews, et
/// les reconstruire ici obligerait ce module à connaître deux autres read models. Une
/// reconstruction n'efface donc PAS le prix déjà indexé — c'est délibéré : mieux vaut
/// un prix éventuellement en retard qu'un catalogue entier soudain sans prix.
///
/// ELLE NE SUPPRIME RIEN NON PLUS. Un document dont le produit n'existe plus n'est
/// pas retiré : il faudrait pour cela énumérer l'index et interroger Catalog produit
/// par produit. `ProductDeleted` couvre ce cas au fil de l'eau, et un orphelin
/// s'affiche mal, là où une suppression en masse fondée sur une lecture incomplète
/// viderait la vitrine.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="BatchSize">Taille des pages lues dans le catalogue. Bornée côté Catalog.</param>
public sealed record RebuildSearchIndexCommand(int BatchSize = 200) : ICommand<SearchIndexRebuildReport>;

/// <summary>Compte rendu d'une reconstruction : de quoi savoir si elle a fait son travail.</summary>
public sealed record SearchIndexRebuildReport(int ProductsScanned, int DocumentsWritten, int VisibleAfter);

internal sealed class RebuildSearchIndexCommandHandler
    : ICommandHandler<RebuildSearchIndexCommand, SearchIndexRebuildReport>
{
    /// <summary>
    /// Garde-fou de boucle. Une pagination par curseur ne peut pas tourner en rond —
    /// le curseur avance strictement — mais une reconstruction est une opération
    /// longue lancée à la main : mieux vaut qu'elle s'arrête et le dise que de
    /// balayer indéfiniment si une hypothèse se révélait fausse.
    /// </summary>
    private const int MaxPages = 10_000;

    private readonly ICatalogModuleApi _catalog;
    private readonly SearchIndexer _indexer;
    private readonly ILogger<RebuildSearchIndexCommandHandler> _logger;

    public RebuildSearchIndexCommandHandler(
        ICatalogModuleApi catalog,
        SearchIndexer indexer,
        ILogger<RebuildSearchIndexCommandHandler> logger)
    {
        _catalog = catalog;
        _indexer = indexer;
        _logger = logger;
    }

    public async Task<Result<SearchIndexRebuildReport>> Handle(
        RebuildSearchIndexCommand command, CancellationToken cancellationToken)
    {
        Guid? cursor = null;
        var scanned = 0;
        var written = 0;
        var visible = 0;
        var pages = 0;

        // Le parcours s'est-il terminé parce qu'il n'y avait plus rien à lire ?
        // Sans ce drapeau, un catalogue tenant en EXACTEMENT `MaxPages` pages ferait
        // conclure à l'emballement alors que la reconstruction a réussi : `pages`
        // vaut alors le nombre d'itérations, et le test de fin ne saurait pas les
        // distinguer.
        var completed = false;

        _logger.LogInformation("Reconstruction de l'index de recherche : démarrage.");

        while (pages++ < MaxPages)
        {
            var batch = await _catalog.ListForIndexingAsync(cursor, command.BatchSize, cancellationToken);
            if (batch.Count == 0)
            {
                completed = true;
                break;
            }

            foreach (var entry in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await _indexer.ReindexProductAsync(
                    entry.Id,
                    entry.SellerId,
                    entry.CategoryId,
                    entry.Name,
                    entry.Slug,
                    IsVisible(entry.Status),
                    entry.PrimaryImageUrl,
                    cancellationToken);

                scanned++;
                written++;
                if (IsVisible(entry.Status))
                {
                    visible++;
                }
            }

            cursor = batch[^1].Id;
        }

        if (!completed)
        {
            _logger.LogError(
                "Reconstruction de l'index INTERROMPUE au bout de {Pages} pages : le curseur n'avance plus.", MaxPages);
            return Result.Failure<SearchIndexRebuildReport>(Error.Failure(
                "search.rebuild.aborted",
                "La reconstruction a été interrompue par sécurité. Consultez les journaux."));
        }

        _logger.LogInformation(
            "Reconstruction de l'index terminée : {Scanned} produit(s), {Written} document(s) écrit(s), {Visible} visible(s).",
            scanned, written, visible);

        return new SearchIndexRebuildReport(scanned, written, visible);
    }

    /// <summary>
    /// Seul « Active » est visible en vitrine. Même règle que
    /// <c>ProductStatusChangedSearchHandler</c>, et c'est bien la raison pour laquelle
    /// la migration de visibilité était fautive : elle avait mis TOUT l'existant à
    /// visible, brouillons compris, et rien ne pouvait corriger ces lignes puisque la
    /// visibilité n'est mise à jour que sur un CHANGEMENT de statut, qu'un produit
    /// resté en brouillon n'émettra jamais. Cette reconstruction les corrige.
    /// </summary>
    private static bool IsVisible(string status)
        => string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);
}
