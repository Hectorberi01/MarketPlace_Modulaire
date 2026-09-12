using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Categories;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Categories.Commands.UpdateCategory;

/// <summary>
/// Recharge le chemin du parent pour recalculer le chemin matérialisé, vérifie
/// l'unicité du slug si le nom change, applique la mise à jour puis persiste.
/// </summary>
internal sealed class UpdateCategoryCommandHandler : ICommandHandler<UpdateCategoryCommand>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly CategorySchemaResolver _resolver;
    private readonly CategorySchemaCompatibilityGuard _compatibility;
    private readonly ICacheService _cache;

    public UpdateCategoryCommandHandler(
        ICategoryRepository categoryRepository,
        ICatalogUnitOfWork unitOfWork,
        CategorySchemaResolver resolver,
        CategorySchemaCompatibilityGuard compatibility,
        ICacheService cache)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
        _resolver = resolver;
        _compatibility = compatibility;
        _cache = cache;
    }

    public async Task<Result> Handle(UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetByIdAsync(new CategoryId(command.CategoryId), cancellationToken);
        if (category is null)
        {
            return Result.Failure(Error.NotFound("catalog.category.not_found", $"Catégorie {command.CategoryId} introuvable."));
        }

        var slugResult = Slug.Create(command.Name);
        if (slugResult.IsFailure)
        {
            return Result.Failure(slugResult.Error);
        }

        // Le chemin du parent doit être connu AVANT le contrôle d'unicité : c'est lui
        // qui distingue « /animaux/chiens/alimentation » de « /animaux/chats/alimentation ».
        string? parentPath = null;
        if (category.ParentId is { } parentId)
        {
            var parent = await _categoryRepository.GetByIdAsync(new CategoryId(parentId), cancellationToken);
            parentPath = parent?.Path;
        }

        // `excludeId` : sans lui, enregistrer une catégorie sans changer son nom la
        // ferait entrer en conflit avec elle-même.
        var newPath = Category.BuildPath(parentPath, slugResult.Value.Value);
        if (await _categoryRepository.PathExistsAsync(newPath, category.Id.Value, cancellationToken))
        {
            return Result.Failure(Error.Conflict(
                "catalog.category.path_taken",
                $"Une catégorie « {command.Name} » existe déjà à cet emplacement."));
        }

        // ═════════════════════════════════════════════════════════════════════════
        // DEUX CONTRÔLES AVANT D'ÉCRIRE, ET ILS SE FONT DANS CET ORDRE.
        //
        // Le schéma demandé est lu ICI plutôt qu'après `Update` : les deux contrôles
        // qui suivent ont besoin de la forme lue, et il serait absurde de muter
        // l'agrégat pour découvrir ensuite qu'on doit refuser.
        //
        //   1. COLLISION AVEC UN ANCÊTRE — une clé héritée ne se redéclare pas.
        //   2. COMPATIBILITÉ AVEC LES FICHES EXISTANTES — on ne durcit pas un schéma
        //      contre des produits déjà en ligne. C'est le contrôle dont l'absence
        //      produit la panne la plus sournoise de tout ce domaine : des centaines
        //      de fiches qui deviennent non modifiables, et un vendeur qui reçoit un
        //      refus parlant d'un attribut qu'il n'a jamais touché.
        // ═════════════════════════════════════════════════════════════════════════
        var demande = CategoryAttributeSchema.Parse(command.AttributeSchema);
        if (demande.IsFailure)
        {
            return Result.Failure(demande.Error);
        }

        var collision = await _resolver.EnsureNoAncestorKeyClashAsync(parentPath, demande.Value, cancellationToken);
        if (collision.IsFailure)
        {
            return collision;
        }

        var actuel = category.ParseSchema();
        if (actuel.IsFailure)
        {
            // Le schéma actuel est illisible : on ne peut pas comparer, donc pas
            // garantir la compatibilité. On laisse tout de même passer — refuser
            // enfermerait la catégorie dans son état cassé, alors que l'écriture en
            // cours est justement ce qui peut la réparer.
            var reparation = category.Update(command.Name, parentPath, command.ImageUrl, command.AttributeSchema);
            if (reparation.IsFailure)
            {
                return reparation;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }

        var compatible = await _compatibility.EnsureCompatibleAsync(
            category, actuel.Value, demande.Value, cancellationToken);
        if (compatible.IsFailure)
        {
            return compatible;
        }

        // Les descendants sont chargés AVANT la mise à jour : on a besoin de l'ANCIEN
        // chemin pour les retrouver, et il aura changé après.
        var descendants = await _categoryRepository.ListDescendantsAsync(category.Path, cancellationToken);
        var ancienChemin = category.Path;

        var updateResult = category.Update(command.Name, parentPath, command.ImageUrl, command.AttributeSchema);
        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        var repercussion = await RepercuterLeChemin(category, ancienChemin, descendants, cancellationToken);
        if (repercussion.IsFailure)
        {
            return repercussion;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await EvincerLaDescendance(descendants, cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// UN RENOMMAGE DÉPLACE TOUTE LA BRANCHE. IL NE LE FAISAIT PAS.
    ///
    /// Le chemin matérialisé du parent était recalculé, celui des descendants non :
    /// renommer « Informatique » laissait ses enfants sur « /informatique/… » pendant
    /// que lui passait à « /informatique-reseaux ». Tant que le chemin ne servait qu'à
    /// l'affichage, c'était cosmétique.
    ///
    /// AVEC L'HÉRITAGE, C'EST UNE PERTE DE CONTRAINTE SILENCIEUSE. Les ancêtres se
    /// retrouvent par le chemin : une branche désaccordée ne remonte plus rien, la
    /// composition n'échoue pas — elle compose avec moins d'étages — et les attributs
    /// hérités disparaissent d'un coup, sans erreur, sans trace.
    ///
    /// L'UNICITÉ EST REVÉRIFIÉE POUR CHAQUE NOUVEAU CHEMIN. Renommer peut faire entrer
    /// une branche entière en collision avec une autre ; l'index unique refuserait à
    /// l'insertion, mais avec une erreur de base illisible. On refuse avant, en nommant
    /// la catégorie fautive.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private async Task<Result> RepercuterLeChemin(
        Category category,
        string ancienChemin,
        IReadOnlyList<Category> descendants,
        CancellationToken cancellationToken)
    {
        if (string.Equals(ancienChemin, category.Path, StringComparison.Ordinal) || descendants.Count == 0)
        {
            return Result.Success();
        }

        foreach (var descendant in descendants)
        {
            var rebase = descendant.RebasePath(ancienChemin, category.Path);
            if (rebase.IsFailure)
            {
                return rebase;
            }

            if (await _categoryRepository.PathExistsAsync(descendant.Path, descendant.Id.Value, cancellationToken))
            {
                return Result.Failure(Error.Conflict(
                    "catalog.category.path_taken",
                    $"Ce renommage placerait « {descendant.Name} » sur un chemin déjà occupé "
                    + $"(« {descendant.Path} »). Renommez d'abord la catégorie qui l'occupe."));
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LE CACHE DES DESCENDANTS PORTE LE SCHÉMA DE LEURS ANCÊTRES.
    ///
    /// L'invalidateur du DbContext évince la catégorie MODIFIÉE et la liste complète.
    /// C'était exact tant qu'une entrée ne dépendait que d'elle-même ; avec l'héritage,
    /// `catalog:category:{id}` d'une feuille contient le schéma composé de toute sa
    /// branche. Ajouter un attribut obligatoire sur « Informatique » laissait donc les
    /// feuilles servir l'ancien schéma pendant tout le TTL.
    ///
    /// LA CONSÉQUENCE N'EST PAS UN AFFICHAGE EN RETARD. Le contrôle des produits, lui,
    /// lit la base sans passer par le cache : le vendeur remplissait un formulaire qui
    /// ne lui demandait pas le nouvel attribut, et se faisait refuser sur un champ
    /// qu'on ne lui avait jamais montré. C'est exactement la panne que tout ce
    /// dispositif existe pour éviter.
    ///
    /// APRÈS LE SaveChanges, ET SANS FAIRE ÉCHOUER L'ÉCRITURE. Une éviction qui rate
    /// laisse des lectures périmées le temps d'un TTL ; annuler une modification déjà
    /// persistée pour autant serait un remède pire que le mal.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private async Task EvincerLaDescendance(
        IReadOnlyList<Category> descendants, CancellationToken cancellationToken)
    {
        if (descendants.Count == 0)
        {
            return;
        }

        await _cache.RemoveManyAsync(
            descendants.Select(d => CatalogCacheKeys.Category(d.Id.Value)),
            cancellationToken);
    }
}
