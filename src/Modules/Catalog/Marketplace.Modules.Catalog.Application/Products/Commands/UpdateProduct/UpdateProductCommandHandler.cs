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

        // LA MODIFICATION EST CONTRÔLÉE COMME LA CRÉATION.
        //
        // La catégorie lue est celle du produit : cette commande ne permet pas d'en
        // changer. Un produit conforme à son schéma à la création pouvait sinon
        // devenir non conforme à la première modification, ce qui aurait fait du
        // contrôle une formalité de premier passage.
        var attributes = await _attributeGuard.ResolveAsync(
            product.CategoryId, command.Attributes, cancellationToken);
        if (attributes.IsFailure)
        {
            return Result.Failure(attributes.Error);
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
