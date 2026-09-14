using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Categories;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Categories.Commands.ImportCategories;

/// <summary>
/// Parcourt chaque chemin segment par segment, réutilise ce qui existe, crée ce qui
/// manque, et rend compte de tout.
/// </summary>
internal sealed class ImportCategoriesCommandHandler
    : ICommandHandler<ImportCategoriesCommand, CategoryImportReport>
{
    private const int MaxDepth = 6;

    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public ImportCategoriesCommandHandler(ICategoryRepository categoryRepository, ICatalogUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CategoryImportReport>> Handle(
        ImportCategoriesCommand command, CancellationToken cancellationToken)
    {
        // ─────────────────────────────────────────────────────────────────────────
        // TOUT L'ARBRE EST CHARGÉ UNE FOIS, EN MÉMOIRE.
        //
        // Un import de quelques centaines de lignes traverse des milliers de nœuds.
        // Interroger la base à chaque segment produirait autant d'allers-retours —
        // insupportable dès lors que la base est distante (une dizaine de
        // millisecondes chacun).
        //
        // Une taxonomie de marketplace compte quelques milliers d'entrées au plus :
        // elle tient sans peine en mémoire. Le dictionnaire est indexé par CHEMIN,
        // jamais par nom — c'est ce qui permet à « Alimentation » d'exister sous
        // « Chiens » et sous « Chats ».
        // ─────────────────────────────────────────────────────────────────────────
        var existing = await _categoryRepository.ListAllAsync(cancellationToken);
        var byPath = existing.ToDictionary(
            c => c.Path,
            c => (Id: c.Id.Value, Path: c.Path),
            StringComparer.OrdinalIgnoreCase);

        var outcomes = new List<CategoryImportOutcome>();
        // Un même nœud est traversé par toutes les lignes de sa branche : sans ce
        // registre, « Animaux » apparaîtrait autant de fois qu'il a de descendants.
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in command.Rows)
        {
            var segments = (row.Path ?? string.Empty)
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (segments.Length == 0)
            {
                AddOutcome(outcomes, reported, key: $"?{outcomes.Count}", path: string.Empty,
                    label: row.Path ?? string.Empty, status: "error", message: "Chemin vide.");
                continue;
            }

            if (segments.Length > MaxDepth)
            {
                AddOutcome(outcomes, reported, key: row.Path!, path: string.Empty, label: row.Path!,
                    status: "error", message: $"Profondeur {segments.Length} — {MaxDepth} niveaux au maximum.");
                continue;
            }

            string? parentPath = null;
            Guid? parentId = null;
            var abort = false;

            for (var i = 0; i < segments.Length && !abort; i++)
            {
                var name = segments[i];
                var label = string.Join(" / ", segments.Take(i + 1));

                var slugResult = Slug.Create(name);
                if (slugResult.IsFailure)
                {
                    AddOutcome(outcomes, reported, key: label, path: string.Empty, label: label,
                        status: "error", message: $"« {name} » ne produit aucun identifiant exploitable.");
                    abort = true; // la suite de la branche dépend de ce segment
                    break;
                }

                var path = Category.BuildPath(parentPath, slugResult.Value.Value);

                if (byPath.TryGetValue(path, out var found))
                {
                    AddOutcome(outcomes, reported, key: path, path: path, label: label, status: "existing", message: null);
                    parentPath = found.Path;
                    parentId = found.Id;
                    continue;
                }

                // L'image ET le schéma d'attributs ne concernent que le DERNIER
                // segment de la ligne. Les niveaux intermédiaires sont créés au
                // passage, sans rien porter : les attributs communs d'un rayon se
                // déclarent en écrivant une ligne POUR le rayon, qui les transmet à
                // toute sa descendance par héritage.
                var isLeaf = i == segments.Length - 1;

                var createResult = Category.Create(
                    name, parentId, parentPath,
                    isLeaf ? row.ImageUrl : null,
                    isLeaf ? row.AttributeSchema : null);

                if (createResult.IsFailure)
                {
                    AddOutcome(outcomes, reported, key: label, path: string.Empty, label: label,
                        status: "error", message: createResult.Error.Message);
                    abort = true;
                    break;
                }

                var created = createResult.Value;

                if (!command.DryRun)
                {
                    await _categoryRepository.AddAsync(created, cancellationToken);
                }

                // Inscrit même en simulation : les lignes suivantes de la même branche
                // doivent le voir comme déjà pris en charge, sinon le compte rendu
                // annoncerait plusieurs créations pour un seul nœud.
                byPath[created.Path] = (created.Id.Value, created.Path);
                AddOutcome(outcomes, reported, key: created.Path, path: created.Path, label: label,
                    status: "created", message: null);

                parentPath = created.Path;
                parentId = created.Id.Value;
            }
        }

        if (!command.DryRun && outcomes.Any(o => o.Status == "created"))
        {
            // Une seule transaction pour tout le fichier : un import à moitié appliqué
            // laisserait une taxonomie tronquée, sans moyen de savoir où reprendre.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new CategoryImportReport(
            outcomes,
            Created: outcomes.Count(o => o.Status == "created"),
            Existing: outcomes.Count(o => o.Status == "existing"),
            Errors: outcomes.Count(o => o.Status == "error"),
            DryRun: command.DryRun);
    }

    private static void AddOutcome(
        List<CategoryImportOutcome> outcomes, HashSet<string> reported,
        string key, string path, string label, string status, string? message)
    {
        if (!reported.Add(key))
        {
            return;
        }

        outcomes.Add(new CategoryImportOutcome(path, label, status, message));
    }
}
