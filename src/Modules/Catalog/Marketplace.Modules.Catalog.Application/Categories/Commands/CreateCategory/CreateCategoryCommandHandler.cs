using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Categories;

namespace Marketplace.Modules.Catalog.Application.Categories.Commands.CreateCategory;

internal sealed class CreateCategoryCommandHandler : ICommandHandler<CreateCategoryCommand, Guid>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly CategorySchemaResolver _resolver;

    public CreateCategoryCommandHandler(
        ICategoryRepository categoryRepository,
        ICatalogUnitOfWork unitOfWork,
        CategorySchemaResolver resolver)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
        _resolver = resolver;
    }

    public async Task<Result<Guid>> Handle(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        // Le chemin matérialisé se construit à partir du parent : on le charge.
        string? parentPath = null;
        if (command.ParentId is { } parentId && parentId != Guid.Empty)
        {
            var parent = await _categoryRepository.GetByIdAsync(new CategoryId(parentId), cancellationToken);
            if (parent is null)
            {
                return Error.NotFound("catalog.category.parent_not_found", $"Catégorie parente {parentId} introuvable.");
            }

            parentPath = parent.Path;
        }

        var result = Category.Create(command.Name, command.ParentId, parentPath, command.ImageUrl, command.AttributeSchema);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        var category = result.Value;

        // ═════════════════════════════════════════════════════════════════════════
        // UNE CLÉ DÉJÀ PORTÉE PAR UN ANCÊTRE EST REFUSÉE ICI, PAS PLUS TARD.
        //
        // `Category.Create` a déjà validé la FORME du schéma ; ce qu'elle ne peut pas
        // savoir, c'est ce que portent les parents — le domaine ne lit pas la base.
        // Sans ce contrôle, une sous-catégorie pouvait redéclarer « marque » en
        // facultatif alors que son parent l'exige, et la question « cet attribut
        // est-il obligatoire ? » n'aurait plus eu de réponse stable : elle aurait
        // dépendu de l'étage d'où l'on regarde.
        // ═════════════════════════════════════════════════════════════════════════
        var schemaLu = category.ParseSchema();
        if (schemaLu.IsFailure)
        {
            return Result.Failure<Guid>(schemaLu.Error);
        }

        var collision = await _resolver.EnsureNoAncestorKeyClashAsync(parentPath, schemaLu.Value, cancellationToken);
        if (collision.IsFailure)
        {
            return Result.Failure<Guid>(collision.Error);
        }

        // Unicité par CHEMIN, pas par slug : « Alimentation » peut exister sous
        // « Chiens » et sous « Chats », mais pas deux fois sous le même parent.
        if (await _categoryRepository.PathExistsAsync(category.Path, excludeId: null, cancellationToken))
        {
            return Error.Conflict(
                "catalog.category.path_taken",
                $"Une catégorie « {category.Name} » existe déjà à cet emplacement.");
        }

        await _categoryRepository.AddAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return category.Id.Value;
    }
}
