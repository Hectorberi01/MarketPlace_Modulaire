using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.RemoveProductMedia;

/// <summary>
/// Retire un média d'un produit ET le supprime côté service média externe. La
/// suppression distante est tentée AVANT de persister : si elle échoue, le média
/// reste attaché (cohérence base ↔ service média).
/// </summary>
public sealed record RemoveProductMediaCommand(Guid ProductId, Guid MediaId) : ICommand;

internal sealed class RemoveProductMediaCommandHandler : ICommandHandler<RemoveProductMediaCommand>
{
    private readonly IProductRepository _productRepository;
    private readonly IMediaStorage _mediaStorage;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public RemoveProductMediaCommandHandler(
        IProductRepository productRepository, IMediaStorage mediaStorage, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _mediaStorage = mediaStorage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RemoveProductMediaCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Result.Failure(Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable."));
        }

        var removed = product.RemoveMedia(command.MediaId);
        if (removed.IsFailure)
        {
            return Result.Failure(removed.Error);
        }

        // Suppression côté service média (idempotente). Si elle échoue, on n'écrit
        // rien en base : le média reste cohérent entre les deux systèmes.
        var deletion = await _mediaStorage.DeleteAsync(removed.Value.ExternalId ?? string.Empty, cancellationToken);
        if (deletion.IsFailure)
        {
            return deletion;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
