using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Catalog.Contracts.IntegrationEvents;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.DeleteProduct;

/// <summary>Charge le produit puis le supprime ; renvoie NotFound s'il n'existe pas.</summary>
internal sealed class DeleteProductCommandHandler : ICommandHandler<DeleteProductCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IMediaStorage _mediaStorage;

    public DeleteProductCommandHandler(
        IProductRepository productRepository,
        ICatalogUnitOfWork unitOfWork,
        IIntegrationEventPublisher publisher,
        IMediaStorage mediaStorage)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _publisher = publisher;
        _mediaStorage = mediaStorage;
    }

    public async Task<Result> Handle(DeleteProductCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Result.Failure(Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable."));
        }

        // Contrainte : supprimer un produit efface aussi toutes ses images sur le
        // stockage (Cloudflare R2). Fail-soft : un échec distant ne bloque pas la
        // suppression du produit (le fichier pourra être nettoyé ultérieurement).
        foreach (var media in product.Media)
        {
            if (!string.IsNullOrWhiteSpace(media.ExternalId))
            {
                await _mediaStorage.DeleteAsync(media.ExternalId, cancellationToken);
            }
        }

        _productRepository.Remove(product);
        // Propage la suppression (via l'outbox, même transaction) : Search retire
        // le produit de l'index, sinon des entrées orphelines persistent.
        await _publisher.PublishAsync(new ProductDeletedIntegrationEvent { ProductId = command.ProductId }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
