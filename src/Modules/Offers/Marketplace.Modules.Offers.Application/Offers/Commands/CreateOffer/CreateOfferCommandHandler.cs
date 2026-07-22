using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Contracts;
using Marketplace.Modules.Offers.Application.Abstractions;
using Marketplace.Modules.Offers.Application.Offers;
using Marketplace.Modules.Offers.Domain.Offers;
using Marketplace.Modules.Sellers.Contracts;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.CreateOffer;

/// <summary>
/// Crée une offre après vérifications IN-PROCESS : le produit existe (Catalog) et
/// le vendeur est actif (Sellers) — via leurs Contracts, jamais d'accès direct à
/// leurs bases. Refuse les doublons (un vendeur ne propose qu'une offre par SKU).
/// </summary>
internal sealed class CreateOfferCommandHandler : ICommandHandler<CreateOfferCommand, Guid>
{
    private readonly IOfferRepository _offerRepository;
    private readonly ICatalogModuleApi _catalogModuleApi;
    private readonly ISellerModuleApi _sellerModuleApi;
    private readonly IOffersUnitOfWork _unitOfWork;
    private readonly PricingOptions _pricingOptions;

    public CreateOfferCommandHandler(
        IOfferRepository offerRepository,
        ICatalogModuleApi catalogModuleApi,
        ISellerModuleApi sellerModuleApi,
        IOffersUnitOfWork unitOfWork,
        PricingOptions pricingOptions)
    {
        _offerRepository = offerRepository;
        _catalogModuleApi = catalogModuleApi;
        _sellerModuleApi = sellerModuleApi;
        _unitOfWork = unitOfWork;
        _pricingOptions = pricingOptions;
    }

    public async Task<Result<Guid>> Handle(CreateOfferCommand command, CancellationToken cancellationToken)
    {
        var product = await _catalogModuleApi.GetProductAsync(command.ProductId, cancellationToken);
        if (product is null)
        {
            return Error.NotFound("offers.offer.product_not_found", "Produit introuvable.");
        }

        // Un vendeur peut PRÉPARER son catalogue (produit + offre) dès l'inscription,
        // même KYB en cours (statut « Pending ») : le produit reste en brouillon et
        // n'est pas achetable tant qu'il n'est pas publié — et la publication, elle,
        // exige un compte actif. On refuse donc seulement les comptes fermés/suspendus.
        // (« KYB pour publier et être payé », pas pour créer — cf. onboarding vendeur.)
        var seller = await _sellerModuleApi.GetSellerAsync(command.SellerId, cancellationToken);
        if (seller is null)
        {
            return Error.NotFound("offers.offer.seller_not_found", "Vendeur introuvable.");
        }
        if (seller.Status is not ("Active" or "Pending"))
        {
            return Error.Forbidden("offers.offer.seller_inactive", "Votre compte ne permet pas encore de créer des offres.");
        }

        var skuResult = Sku.Create(command.Sku);
        if (skuResult.IsFailure)
        {
            return Result.Failure<Guid>(skuResult.Error);
        }

        if (!Enum.TryParse<OfferCondition>(command.Condition, ignoreCase: true, out var condition))
        {
            return Error.Validation("offers.offer.condition_invalid", "Condition invalide.");
        }

        if (!Enum.TryParse<FulfillmentType>(command.FulfillmentType, ignoreCase: true, out var fulfillment))
        {
            return Error.Validation("offers.offer.fulfillment_invalid", "Mode de fulfillment invalide.");
        }

        if (await _offerRepository.ExistsAsync(command.SellerId, skuResult.Value.Value, cancellationToken))
        {
            return Error.Conflict("offers.offer.duplicate", "Ce vendeur a déjà une offre pour ce SKU.");
        }

        var result = Offer.Create(
            command.ProductId,
            command.SellerId,
            skuResult.Value,
            command.SellerPrice,
            _pricingOptions.PlatformCommissionRate,
            _pricingOptions.ProviderFeeRate,
            command.Currency,
            condition,
            fulfillment,
            command.ShipFromLocationId,
            command.HandlingTime);

        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _offerRepository.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result.Value.Id.Value;
    }
}
