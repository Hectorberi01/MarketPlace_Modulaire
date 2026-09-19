using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.UpdateProduct;

/// <summary>
/// Charge le produit, vérifie l'unicité du slug si le nom a changé, applique la
/// mise à jour métier puis persiste.
/// </summary>
internal sealed class UpdateProductCommandHandler : ICommandHandler<UpdateProductCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly ProductAttributeGuard _attributeGuard;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public UpdateProductCommandHandler(
        IProductRepository productRepository,
        ProductAttributeGuard attributeGuard,
        ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _attributeGuard = attributeGuard;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Result.Failure(Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable."));
        }

        var slugResult = Slug.Create(command.Name);
        if (slugResult.IsFailure)
        {
            return Result.Failure(slugResult.Error);
        }

        var newSlug = slugResult.Value.Value;
        if (newSlug != product.Slug.Value && await _productRepository.SlugExistsAsync(newSlug, cancellationToken))
        {
            return Result.Failure(Error.Conflict("catalog.product.slug_taken", $"Le slug « {newSlug} » existe déjà."));
        }

        // ════════════════════════════════════════════════════════════════════════
        // LA CATÉGORIE PEUT CHANGER, ET LES ATTRIBUTS SONT ALORS VALIDÉS CONTRE LA NOUVELLE.
        //
        // Elle était figée à la création, sans aucun moyen de la corriger — ni pour le
        // vendeur, ni pour l'administration. Une veste rangée dans « Informatique » y
        // restait, introuvable pour qui parcourt le bon rayon.
        //
        // `ProductAttributeGuard` prenait DÉJÀ un identifiant de catégorie en paramètre :
        // lui passer la nouvelle suffit à ce que les caractéristiques obligatoires soient
        // celles du rayon d'arrivée. Un produit déplacé sans ses attributs est refusé,
        // pas accepté à moitié.
        // ════════════════════════════════════════════════════════════════════════
        var categorieCible = command.CategoryId is { } demandee && demandee != Guid.Empty
            ? demandee
            : product.CategoryId;

        var deplacement = categorieCible != product.CategoryId;

        // `forCreation` SUIT LE DÉPLACEMENT, et c'est le seul point délicat ici.
        //
        // Ce drapeau commande l'exigence de FEUILLE. Le garde l'a mise à faux en
        // modification pour une bonne raison : un produit peut se retrouver dans une
        // catégorie intermédiaire parce que l'administration a ajouté une sous-catégorie
        // sous lui, et l'enfermer punirait le vendeur d'une décision qui n'est pas la
        // sienne.
        //
        // Ce raisonnement ne vaut PAS pour un déplacement : là, le vendeur choisit, et
        // choisir une catégorie qui a des enfants revient à se priver des attributs par
        // lesquels les acheteurs filtrent — sans le savoir.
        var attributes = await _attributeGuard.ResolveAsync(
            categorieCible, command.Attributes, cancellationToken, forCreation: deplacement);
        if (attributes.IsFailure)
        {
            return Result.Failure(attributes.Error);
        }

        // AVANT `Update`, et l'ordre compte : `Update` lève l'événement de mise à jour
        // avec la catégorie courante. Appelé après, il réindexerait l'ancienne.
        if (deplacement)
        {
            var deplace = product.ChangeCategory(categorieCible);
            if (deplace.IsFailure)
            {
                return deplace;
            }
        }

        var updateResult = product.Update(
            command.Name,
            command.Description,
            command.BrandId,
            command.Gtin,
            command.Ean,
            command.ProductGroupId,
            attributes.Value,
            command.Tags);

        if (updateResult.IsFailure)
        {
            return updateResult;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
