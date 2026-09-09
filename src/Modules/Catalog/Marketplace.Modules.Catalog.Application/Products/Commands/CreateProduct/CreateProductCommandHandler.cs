using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.CreateProduct;

internal sealed class CreateProductCommandHandler : ICommandHandler<CreateProductCommand, Guid>
{
    private readonly IProductRepository _productRepository;
    private readonly ProductAttributeGuard _attributeGuard;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public CreateProductCommandHandler(
        IProductRepository productRepository,
        ProductAttributeGuard attributeGuard,
        ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _attributeGuard = attributeGuard;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {
        // La catégorie et son schéma d'attributs, AVANT le slug : c'est le contrôle
        // qui peut refuser, et il ne coûte rien de le faire d'abord.
        var attributes = await _attributeGuard.ResolveAsync(
            command.CategoryId, command.Attributes, cancellationToken);
        if (attributes.IsFailure)
        {
            return Result.Failure<Guid>(attributes.Error);
        }

        // Slug UNIQUE résolu ici : deux produits homonymes (ou une reprise après un
        // échec partiel) ne doivent pas bloquer la création — on suffixe « -2 », « -3 »…
        var slugResult = await ResolveUniqueSlugAsync(command.Name, cancellationToken);
        if (slugResult.IsFailure)
        {
            return Result.Failure<Guid>(slugResult.Error);
        }

        var result = Product.Create(
            command.SellerId,
            command.CategoryId,
            command.Name,
            command.Description,
            command.BrandId,
            command.Gtin,
            command.Ean,
            command.ProductGroupId,
            attributes.Value,
            command.Tags,
            slug: slugResult.Value);

        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        var product = result.Value;

        await _productRepository.AddAsync(product, cancellationToken);

        // SaveChanges dispatche le ProductCreatedDomainEvent, dont le handler
        // écrit l'IntegrationEvent dans l'outbox — le tout dans une transaction.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return product.Id.Value;
    }

    /// <summary>
    /// Renvoie un slug LIBRE dérivé du nom. Si le slug de base est pris, on tente
    /// « base-2 », « base-3 »… puis, en dernier recours, un suffixe aléatoire.
    /// </summary>
    private async Task<Result<Slug>> ResolveUniqueSlugAsync(string name, CancellationToken ct)
    {
        var baseResult = Slug.Create(name);
        if (baseResult.IsFailure)
        {
            return baseResult;
        }

        var baseValue = baseResult.Value.Value;
        if (!await _productRepository.SlugExistsAsync(baseValue, ct))
        {
            return baseResult.Value;
        }

        for (var n = 2; n <= 100; n++)
        {
            var candidate = Slug.Create($"{baseValue}-{n}");
            if (candidate.IsFailure)
            {
                return candidate;
            }

            if (!await _productRepository.SlugExistsAsync(candidate.Value.Value, ct))
            {
                return candidate.Value;
            }
        }

        // Collision après 100 homonymes : suffixe aléatoire (quasi impossible à recroiser).
        var suffixed = $"{baseValue}-{Guid.NewGuid():N}";
        return Slug.Create(suffixed[..Math.Min(suffixed.Length, 200)]);
    }
}
