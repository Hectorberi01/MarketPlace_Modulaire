using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.AddProductMedia;

internal sealed class AddProductMediaCommandHandler : ICommandHandler<AddProductMediaCommand, Guid>
{
    private readonly IProductRepository _productRepository;
    private readonly IMediaUrlPolicy _urlPolicy;
    private readonly ICatalogUnitOfWork _unitOfWork;

    public AddProductMediaCommandHandler(
        IProductRepository productRepository, IMediaUrlPolicy urlPolicy, ICatalogUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _urlPolicy = urlPolicy;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(AddProductMediaCommand command, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdAsync(new ProductId(command.ProductId), cancellationToken);
        if (product is null)
        {
            return Error.NotFound("catalog.product.not_found", $"Produit {command.ProductId} introuvable.");
        }

        if (!Enum.TryParse<ProductMediaType>(command.Type, ignoreCase: true, out var mediaType))
        {
            return Error.Validation("catalog.media.type_invalid", "Type de média invalide (Image ou Video).");
        }

        // ═════════════════════════════════════════════════════════════════════════
        // LA PLACE NE SERT QUE DES MÉDIAS QU'ELLE HÉBERGE.
        //
        // Cette URL part telle quelle dans la fiche produit ANONYME : c'est le
        // navigateur de l'acheteur qui ira la chercher. Une URL arbitraire, c'est
        // l'adresse IP de chaque visiteur relevée par un tiers, et un contenu qui
        // peut changer APRÈS le passage du modérateur — auquel cas « modéré » ne
        // veut plus rien dire.
        //
        // Le message nomme les hôtes acceptés : refuser sans dire quoi corriger
        // n'aurait renvoyé le vendeur nulle part.
        // ═════════════════════════════════════════════════════════════════════════
        if (!_urlPolicy.IsAllowed(command.Url))
        {
            return Error.Validation(
                "catalog.media.url_not_allowed",
                $"Cette adresse ne peut pas être attachée : seuls les médias hébergés par la place sont acceptés "
                + $"({_urlPolicy.AllowedDescription}). Téléversez le fichier plutôt que d'en donner l'adresse.");
        }

        // Aucune clé de stockage : ce média n'a pas été écrit par nous, sa
        // suppression ne doit donc rien tenter à distance.
        var result = product.AddMedia(command.Url, mediaType, command.AltText, command.IsPrimary, externalId: null);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result.Value.Id;
    }
}
