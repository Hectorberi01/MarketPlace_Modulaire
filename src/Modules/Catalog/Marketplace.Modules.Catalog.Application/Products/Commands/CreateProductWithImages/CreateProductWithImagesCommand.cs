using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.CreateProductWithImages;

/// <summary>Image fournie à la création du produit (contenu binaire).</summary>
public sealed record ProductImageUpload(string FileName, string ContentType, byte[] Content, string? AltText = null);

/// <summary>Résultat : id du produit créé + URLs publiques des images stockées.</summary>
public sealed record ProductWithImagesResult(Guid ProductId, IReadOnlyList<string> ImageUrls);

/// <summary>
/// Crée un produit (Draft) et téléverse ses images vers le service média externe,
/// puis associe les URLs renvoyées au produit (la première image devient
/// principale). Les uploads sont faits AVANT la persistance : si un upload échoue,
/// rien n'est créé.
/// </summary>
public sealed record CreateProductWithImagesCommand(
    Guid SellerId,
    Guid CategoryId,
    string Name,
    string Description,
    Guid? BrandId,
    string? Gtin,
    string? Ean,
    Guid? ProductGroupId,
    IReadOnlyDictionary<string, string>? Attributes,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<ProductImageUpload> Images) : ICommand<ProductWithImagesResult>;

internal sealed class CreateProductWithImagesCommandHandler
    : ICommandHandler<CreateProductWithImagesCommand, ProductWithImagesResult>
{
    private readonly IProductRepository _productRepository;
    private readonly IMediaStorage _mediaStorage;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public CreateProductWithImagesCommandHandler(
        IProductRepository productRepository, IMediaStorage mediaStorage, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _mediaStorage = mediaStorage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ProductWithImagesResult>> Handle(
        CreateProductWithImagesCommand command, CancellationToken cancellationToken)
    {
        // 1) Upload des images vers le service média EN UNE SEULE requête « batch »
        //    (avant toute écriture en base : si l'upload échoue, rien n'est créé).
        var uploads = command.Images
            .Select(i => new MediaUpload(i.FileName, i.ContentType, i.Content, i.AltText))
            .ToList();

        var uploadResult = await _mediaStorage.UploadManyAsync(uploads, cancellationToken);
        if (uploadResult.IsFailure)
        {
            return Result.Failure<ProductWithImagesResult>(uploadResult.Error);
        }
        var stored = uploadResult.Value;

        // 2) Création du produit.
        var result = Product.Create(
            command.SellerId, command.CategoryId, command.Name, command.Description,
            command.BrandId, command.Gtin, command.Ean, command.ProductGroupId,
            command.Attributes, command.Tags);
        if (result.IsFailure)
        {
            return Result.Failure<ProductWithImagesResult>(result.Error);
        }

        var product = result.Value;

        if (await _productRepository.SlugExistsAsync(product.Slug.Value, cancellationToken))
        {
            return Error.Conflict("catalog.product.slug_taken", $"Le slug « {product.Slug.Value} » existe déjà.");
        }

        // 3) Association des médias (la 1re image devient principale automatiquement).
        foreach (var media in stored)
        {
            var add = product.AddMedia(media.Url, ProductMediaType.Image, command.Name, isPrimary: false, externalId: media.ExternalId);
            if (add.IsFailure)
            {
                return Result.Failure<ProductWithImagesResult>(add.Error);
            }
        }

        await _productRepository.AddAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProductWithImagesResult(product.Id.Value, stored.Select(m => m.Url).ToList());
    }
}
