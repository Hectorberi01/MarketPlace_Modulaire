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
    private readonly ProductAttributeGuard _attributeGuard;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public CreateProductWithImagesCommandHandler(
        IProductRepository productRepository,
        IMediaStorage mediaStorage,
        ProductAttributeGuard attributeGuard,
        ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _mediaStorage = mediaStorage;
        _attributeGuard = attributeGuard;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ProductWithImagesResult>> Handle(
        CreateProductWithImagesCommand command, CancellationToken cancellationToken)
    {
        // 0) LA CATÉGORIE ET SES ATTRIBUTS, AVANT TOUT TÉLÉVERSEMENT.
        //
        // L'ordre compte ici plus qu'ailleurs : les images partent au stockage objet
        // avant la moindre écriture en base, et rien ne les supprime si la suite
        // échoue. Un refus d'attribut placé après l'upload laisserait donc des objets
        // orphelins, facturés, jamais référencés. Placé avant, il ne coûte rien.
        // `forCreation: true` : c'est ce qui exige une catégorie FEUILLE. La
        // modification d'un produit ne le fait pas — voir ProductAttributeGuard.
        var attributes = await _attributeGuard.ResolveAsync(
            command.CategoryId, command.Attributes, cancellationToken, forCreation: true);
        if (attributes.IsFailure)
        {
            return Result.Failure<ProductWithImagesResult>(attributes.Error);
        }

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

        // 2) Création du produit. Slug UNIQUE résolu ici : deux produits homonymes
        //    (ou une reprise après un échec partiel de l'assistant) ne doivent pas
        //    être bloqués — on suffixe « -2 », « -3 »… au lieu de refuser.
        var slugResult = await ResolveUniqueSlugAsync(command.Name, cancellationToken);
        if (slugResult.IsFailure)
        {
            return Result.Failure<ProductWithImagesResult>(slugResult.Error);
        }

        var result = Product.Create(
            command.SellerId, command.CategoryId, command.Name, command.Description,
            command.BrandId, command.Gtin, command.Ean, command.ProductGroupId,
            attributes.Value, command.Tags, slug: slugResult.Value);
        if (result.IsFailure)
        {
            return Result.Failure<ProductWithImagesResult>(result.Error);
        }

        var product = result.Value;

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

    /// <summary>
    /// Renvoie un slug LIBRE dérivé du nom (« base-2 », « base-3 »… si le nom est
    /// déjà pris), pour ne jamais bloquer la création sur un homonyme.
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

        var suffixed = $"{baseValue}-{Guid.NewGuid():N}";
        return Slug.Create(suffixed[..Math.Min(suffixed.Length, 200)]);
    }
}
