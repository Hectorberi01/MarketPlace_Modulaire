using HBA.Deliveries.Contracts;
using HBA.Deliveries.Domain.Deliveries;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Application.Deliveries.Queries;

/// <summary>
/// Détail d'une course.
///
/// <c>RequiredPartnerId</c> n'est renseigné que par l'API publique : il exige que
/// la course appartienne à ce partenaire. Nul depuis les surfaces internes, où
/// l'appelant est déjà HBA.
/// </summary>
public sealed record GetDeliveryQuery(Guid DeliveryId, Guid? RequiredPartnerId = null)
    : IQuery<DeliverySummary>;

/// <summary>Suivi d'une course : état, et position du livreur pendant le transport.</summary>
public sealed record GetDeliveryTrackingQuery(Guid DeliveryId, Guid? RequiredPartnerId = null)
    : IQuery<DeliveryTracking>;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// LES LECTURES PASSENT PAR L'API DU MODULE, PAS PAR LE DÉPÔT.
///
/// On pourrait interroger <c>IDeliveryRepository</c> ici et projeter à la main.
/// Ce serait une seconde façon de construire un <see cref="DeliverySummary"/> —
/// et deux projections divergent toujours, en général sur un champ ajouté d'un
/// seul côté.
///
/// <c>IDeliveryModuleApi</c> est déjà la forme publique du module, utilisée par
/// les autres modules et par les webhooks partenaires. Ces requêtes ne sont donc
/// qu'un adaptateur MediatR par-dessus, pour que les endpoints HTTP suivent la
/// même convention que les vingt-cinq autres modules.
///
/// LE DÉPÔT REVIENT POUR UNE SEULE CHOSE : L'APPARTENANCE.
///
/// <see cref="DeliverySummary"/> ne porte pas de <c>PartnerId</c>, et il ne doit
/// pas en porter : cette forme part dans les webhooks et vers les autres modules,
/// à qui la facturation ne regarde pas. Le contrôle d'appartenance se fait donc
/// sur l'agrégat, avant la projection.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class DeliveryQueryHandler
    : IQueryHandler<GetDeliveryQuery, DeliverySummary>,
      IQueryHandler<GetDeliveryTrackingQuery, DeliveryTracking>
{
    private readonly IDeliveryModuleApi _api;
    private readonly IDeliveryRepository _repository;

    public DeliveryQueryHandler(IDeliveryModuleApi api, IDeliveryRepository repository)
    {
        _api = api;
        _repository = repository;
    }

    public async Task<Result<DeliverySummary>> Handle(GetDeliveryQuery query, CancellationToken cancellationToken)
    {
        var authorized = await EnsureBelongsToAsync(query.DeliveryId, query.RequiredPartnerId, cancellationToken);
        if (authorized.IsFailure)
        {
            return Result.Failure<DeliverySummary>(authorized.Error);
        }

        var delivery = await _api.GetAsync(query.DeliveryId, cancellationToken);

        return delivery is null
            ? Result.Failure<DeliverySummary>(NotFound)
            : delivery;
    }

    public async Task<Result<DeliveryTracking>> Handle(
        GetDeliveryTrackingQuery query, CancellationToken cancellationToken)
    {
        var authorized = await EnsureBelongsToAsync(query.DeliveryId, query.RequiredPartnerId, cancellationToken);
        if (authorized.IsFailure)
        {
            return Result.Failure<DeliveryTracking>(authorized.Error);
        }

        var tracking = await _api.GetTrackingAsync(query.DeliveryId, cancellationToken);

        return tracking is null
            ? Result.Failure<DeliveryTracking>(NotFound)
            : tracking;
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════
    /// « INTROUVABLE » ET NON « INTERDIT » — C'EST DÉLIBÉRÉ.
    ///
    /// Répondre 403 sur la course d'un autre partenaire confirmerait qu'elle
    /// existe. Un identifiant est un GUID, donc non devinable ; mais une
    /// intégration qui journalise des identifiants, un salarié qui change
    /// d'employeur, une capture d'écran suffisent à en faire circuler quelques-uns.
    /// Un 403 transforme alors chacun en confirmation : « cette course existe, et
    /// elle appartient à un concurrent ».
    ///
    /// 404 ne dit rien. Le partenaire légitime, lui, ne voit jamais la différence.
    /// ═════════════════════════════════════════════════════════════════════════
    /// </summary>
    private async Task<Result> EnsureBelongsToAsync(
        Guid deliveryId, Guid? requiredPartnerId, CancellationToken cancellationToken)
    {
        if (requiredPartnerId is null)
        {
            return Result.Success();
        }

        var delivery = await _repository.GetByIdAsync(new DeliveryId(deliveryId), cancellationToken);

        return delivery is not null && delivery.PartnerId == requiredPartnerId
            ? Result.Success()
            : Result.Failure(NotFound);
    }

    private static Error NotFound => Error.NotFound("delivery.not_found", "Course introuvable.");
}
