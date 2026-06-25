using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Offers.Contracts;
using Marketplace.Modules.Offers.Domain.Offers;

namespace Marketplace.Modules.Offers.Application.Offers.Queries;

/// <summary>Récupère une offre par son identifiant.</summary>
public sealed record GetOfferQuery(Guid OfferId) : IQuery<OfferSummary>;

/// <summary>Liste les offres (vendeurs) attachées à un produit (Buy Box / comparaison).</summary>
public sealed record ListOffersByProductQuery(Guid ProductId) : IQuery<IReadOnlyList<OfferSummary>>;

/// <summary>Liste les offres d'un vendeur (back-office vendeur).</summary>
public sealed record ListOffersBySellerQuery(Guid SellerId) : IQuery<IReadOnlyList<OfferSummary>>;

internal sealed class GetOfferQueryHandler : IQueryHandler<GetOfferQuery, OfferSummary>
{
    private readonly IOfferRepository _offerRepository;

    public GetOfferQueryHandler(IOfferRepository offerRepository) => _offerRepository = offerRepository;

    public async Task<Result<OfferSummary>> Handle(GetOfferQuery query, CancellationToken cancellationToken)
    {
        var offer = await _offerRepository.GetByIdAsync(new OfferId(query.OfferId), cancellationToken);
        return offer is null
            ? Error.NotFound("offers.offer.not_found", $"Offre {query.OfferId} introuvable.")
            : OfferMapper.ToSummary(offer);
    }
}

internal sealed class ListOffersByProductQueryHandler : IQueryHandler<ListOffersByProductQuery, IReadOnlyList<OfferSummary>>
{
    private readonly IOfferRepository _offerRepository;

    public ListOffersByProductQueryHandler(IOfferRepository offerRepository) => _offerRepository = offerRepository;

    public async Task<Result<IReadOnlyList<OfferSummary>>> Handle(ListOffersByProductQuery query, CancellationToken cancellationToken)
    {
        var offers = await _offerRepository.ListByProductAsync(query.ProductId, cancellationToken);
        IReadOnlyList<OfferSummary> summaries = offers.Select(OfferMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}

internal sealed class ListOffersBySellerQueryHandler : IQueryHandler<ListOffersBySellerQuery, IReadOnlyList<OfferSummary>>
{
    private readonly IOfferRepository _offerRepository;

    public ListOffersBySellerQueryHandler(IOfferRepository offerRepository) => _offerRepository = offerRepository;

    public async Task<Result<IReadOnlyList<OfferSummary>>> Handle(ListOffersBySellerQuery query, CancellationToken cancellationToken)
    {
        var offers = await _offerRepository.ListBySellerAsync(query.SellerId, cancellationToken);
        IReadOnlyList<OfferSummary> summaries = offers.Select(OfferMapper.ToSummary).ToList();
        return Result.Success(summaries);
    }
}
