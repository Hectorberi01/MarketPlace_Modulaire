using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.UploadProductImage;

/// <summary>
/// Téléverse une image vers le service média externe puis l'attache au produit.
/// Retourne l'URL publique stockée.
/// </summary>
public sealed record UploadProductImageCommand(
    Guid ProductId,
    string FileName,
    string ContentType,
    byte[] Content,
    string? AltText = null,
    bool IsPrimary = false) : ICommand<string>;

internal sealed class UploadProductImageCommandHandler : ICommandHandler<UploadProductImageCommand, string>
{
    private readonly IProductRepository _productRepository;
    private readonly IMediaStorage _mediaStorage;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public UploadProductImageCommandHandler(
        IProductRepository productRepository, IMediaStorage mediaStorage, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _mediaStorage = mediaStorage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<string>> Handle(UploadProductImageCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable.");
        }

        var upload = await _mediaStorage.UploadAsync(
            new MediaUpload(command.FileName, command.ContentType, command.Content, MediaUsage.ProductImage, command.AltText), cancellationToken);
        if (upload.IsFailure)
        {
            return Result.Failure<string>(upload.Error);
        }

        var added = product.AddMedia(
            upload.Value.Url, ProductMediaType.Image, command.AltText, command.IsPrimary, upload.Value.ExternalId);
        if (added.IsFailure)
        {
            return Result.Failure<string>(added.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(upload.Value.Url);
    }
}
