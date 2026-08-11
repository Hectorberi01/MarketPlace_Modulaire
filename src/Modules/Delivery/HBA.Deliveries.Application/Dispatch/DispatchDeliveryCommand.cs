using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Dispatch;
using HBA.Deliveries.Domain.Drivers;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Microsoft.Extensions.Logging;

namespace HBA.Deliveries.Application.Dispatch;

/// <summary>Résultat d'un tour de dispatch.</summary>
/// <param name="DriverId">Livreur à qui la course a été proposée, s'il y en a un.</param>
/// <param name="CandidatesConsidered">Nombre de candidats éligibles examinés.</param>
/// <param name="RadiusKm">Rayon effectivement utilisé.</param>
public sealed record DispatchOutcome(Guid? DriverId, int CandidatesConsidered, double RadiusKm);

/// <summary>
/// Propose une course au meilleur livreur disponible. Un appel = UNE proposition.
/// </summary>
public sealed record DispatchDeliveryCommand(Guid DeliveryId) : ICommand<DispatchOutcome>;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN TOUR DE DISPATCH.
///
/// POURQUOI UNE SEULE PROPOSITION À LA FOIS
///
/// Il serait tentant de proposer la course à cinq livreurs simultanément et de
/// retenir le premier qui accepte. C'est la « course au clic », et elle coûte
/// cher : quatre livreurs sur cinq se déroutent pour rien, apprennent que
/// répondre vite ne sert à rien, et finissent par ne plus répondre du tout.
///
/// Une proposition à la fois, avec un délai d'expiration court, préserve la
/// confiance — qui est la seule ressource rare d'une flotte de livreurs
/// indépendants.
///
/// POURQUOI LE RAYON S'ÉLARGIT TOUT SEUL
///
/// À Cotonou, cinq kilomètres suffisent en journée. À Parakou, ou à 22 h, non.
/// Plutôt que de configurer un rayon par ville — qu'il faudrait maintenir —, on
/// élargit après les premiers échecs : le système s'adapte à ce qu'il observe.
///
/// CE QUE CE HANDLER NE FAIT PAS
///
/// Il n'attend pas la réponse du livreur, ne pose pas de minuterie et ne
/// réessaie pas. Un service de fond rappellera cette commande tant que la course
/// n'est pas pourvue. Ici, une commande = un effet, observable et testable.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class DispatchDeliveryCommandHandler : ICommandHandler<DispatchDeliveryCommand, DispatchOutcome>
{
    private readonly IDeliveryRepository _deliveries;
    private readonly IDriverRepository _drivers;
    private readonly IDriverLocationCache _locations;
    private readonly IDeliveryUnitOfWork _unitOfWork;
    private readonly ILogger<DispatchDeliveryCommandHandler> _logger;

    public DispatchDeliveryCommandHandler(
        IDeliveryRepository deliveries,
        IDriverRepository drivers,
        IDriverLocationCache locations,
        IDeliveryUnitOfWork unitOfWork,
        ILogger<DispatchDeliveryCommandHandler> logger)
    {
        _deliveries = deliveries;
        _drivers = drivers;
        _locations = locations;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<DispatchOutcome>> Handle(DispatchDeliveryCommand command, CancellationToken cancellationToken)
    {
        var delivery = await _deliveries.GetByIdAsync(new DeliveryId(command.DeliveryId), cancellationToken);
        if (delivery is null)
        {
            return Result.Failure<DispatchOutcome>(
                Error.NotFound("delivery.not_found", "Course introuvable."));
        }

        if (delivery.Status is not DeliveryStatus.SearchingDriver)
        {
            return Result.Failure<DispatchOutcome>(
                Error.Conflict("delivery.not_searching", "Cette course n'est pas en recherche de livreur."));
        }

        // Le rayon s'élargit après deux tentatives infructueuses. Le seuil est bas
        // à dessein : mieux vaut un livreur un peu plus loin qu'un client qui
        // attend sans explication.
        var radiusKm = delivery.DispatchAttempts < 2
            ? DispatchPolicy.DefaultRadiusKm
            : DispatchPolicy.ExtendedRadiusKm;

        var nearby = await _locations.FindNearbyAsync(
            delivery.Pickup.Position, radiusKm, limit: 20, cancellationToken);

        if (nearby.Count == 0)
        {
            return new DispatchOutcome(null, 0, radiusKm);
        }

        // Un seul aller-retour en base pour tous les candidats : le dispatch est le
        // chemin le plus sensible à la latence de toute l'application.
        var drivers = await _drivers.ListByIdsAsync(
            nearby.Select(n => n.DriverId).ToList(), cancellationToken);

        var byId = drivers.ToDictionary(d => d.Id);

        var candidates = nearby
            .Where(n => byId.ContainsKey(n.DriverId))
            .Select(n => new DriverCandidate(byId[n.DriverId], n.Position))
            .ToList();

        var ranked = DispatchPolicy.Rank(delivery, candidates, radiusKm);
        if (ranked.Count == 0)
        {
            // Aucun candidat ÉLIGIBLE, alors que le cache en signalait. C'est
            // normal — véhicule inadapté, déjà en course, refus antérieur — mais
            // si cela se répète, c'est le signe que la flotte ne correspond pas
            // aux courses proposées. On le journalise pour pouvoir le constater.
            _logger.LogInformation(
                "Dispatch {DeliveryId} : {Nearby} livreur(s) à proximité, aucun éligible (rayon {Radius} km, tentative {Attempt}).",
                delivery.Id.Value, nearby.Count, radiusKm, delivery.DispatchAttempts + 1);

            return new DispatchOutcome(null, 0, radiusKm);
        }

        // `First()` pluôt que `ranked[0]` : même résultat, et l'accès passe par
        // System.Linq sur IEnumerable au lieu de l'indexeur d'IReadOnlyList.
        // Le classement est déjà trié, et la liste est non vide — le cas contraire
        // est traité juste au-dessus.
        var best = ranked.First();

        var assigned = delivery.AssignTo(best.DriverId);
        if (assigned.IsFailure)
        {
            return Result.Failure<DispatchOutcome>(assigned.Error);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new DispatchOutcome(best.DriverId.Value, ranked.Count, radiusKm);
    }
}
