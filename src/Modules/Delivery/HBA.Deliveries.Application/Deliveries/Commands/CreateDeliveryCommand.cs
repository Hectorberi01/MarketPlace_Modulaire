using FluentValidation;
using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Partners;
using HBA.Deliveries.Domain.Pricing;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Application.Deliveries.Commands;

/// <summary>Un point de la course, tel que l'appelant le décrit.</summary>
public sealed record DeliveryStopInput(
    string? ContactName,
    string? Phone,
    string? Commune,
    string? Quartier,
    string? Landmark,
    string? Instructions = null,
    double? Latitude = null,
    double? Longitude = null);

/// <summary>Le colis, tel que l'appelant le décrit.</summary>
public sealed record DeliveryPackageInput(
    string? Description,
    decimal? WeightKg = null,
    bool IsFragile = false,
    bool IsPerishable = false);

/// <summary>
/// Crée une course. Point d'entrée commun à HBAExpress, HBA Food et à l'API
/// partenaire — c'est la même opération pour les trois.
/// </summary>
public sealed record CreateDeliveryCommand(
    string Reference,
    DeliverySource Source,
    DeliveryType Type,
    DeliveryStopInput Pickup,
    DeliveryStopInput Dropoff,
    DeliveryPackageInput Package,
    ProofOfDeliveryKind RequiredProof = ProofOfDeliveryKind.None,
    Guid? PartnerId = null,
    string? QuoteId = null,

    // Heure de livraison souhaitée, pour le seul type « Scheduled ». L'agrégat
    // tient l'invariant dans les deux sens : programmée sans date, ou date sur
    // une course qui ne l'est pas, sont refusées.
    DateTime? ScheduledForUtc = null) : ICommand<Guid>;

internal sealed class CreateDeliveryCommandValidator : AbstractValidator<CreateDeliveryCommand>
{
    public CreateDeliveryCommandValidator()
    {
        // Validation de FORME seulement. Les règles métier — commune connue,
        // téléphone béninois valide, repère obligatoire — appartiennent au
        // domaine, qui les applique quel que soit le chemin d'entrée.
        RuleFor(c => c.Reference).NotEmpty().MaximumLength(120);
        RuleFor(c => c.Pickup).NotNull();
        RuleFor(c => c.Dropoff).NotNull();
        RuleFor(c => c.Package).NotNull();
    }
}

internal sealed class CreateDeliveryCommandHandler : ICommandHandler<CreateDeliveryCommand, Guid>
{
    private readonly IDeliveryRepository _repository;
    private readonly IDeliveryQuoteRepository _quotes;
    private readonly IPartnerRepository _partners;
    private readonly IDeliveryUnitOfWork _unitOfWork;

    public CreateDeliveryCommandHandler(
        IDeliveryRepository repository,
        IDeliveryQuoteRepository quotes,
        IPartnerRepository partners,
        IDeliveryUnitOfWork unitOfWork)
    {
        _repository = repository;
        _quotes = quotes;
        _partners = partners;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateDeliveryCommand command, CancellationToken cancellationToken)
    {
        // ─────────────────────────────────────────────────────────────────────
        // IDEMPOTENCE PAR LA RÉFÉRENCE, ET NON PAR UN EN-TÊTE.
        //
        // Un site partenaire qui rejoue sa requête après un délai dépassé doit
        // retrouver SA course, pas en créer une seconde : sinon deux livreurs se
        // déplacent, et le partenaire est facturé deux fois pour une commande.
        //
        // On ne s'appuie pas sur un en-tête « Idempotency-Key » : il est optionnel,
        // souvent oublié par les intégrateurs, et sa fenêtre de validité expire.
        // La référence de commande, elle, est toujours là — c'est justement ce qui
        // identifie l'opération du point de vue de l'appelant.
        //
        // La clé porte AUSSI la source : deux systèmes différents peuvent
        // légitimement émettre une commande nommée « 1024 ».
        // ─────────────────────────────────────────────────────────────────────
        var existing = await _repository.GetByReferenceAsync(command.Reference, command.Source, cancellationToken);
        if (existing is not null)
        {
            // ─────────────────────────────────────────────────────────────────
            // UN REJEU NE DOIT PAS RÉVÉLER LA COURSE D'UN AUTRE.
            //
            // La clé d'idempotence est (référence, source), et la source
            // « ExternalPartner » est COMMUNE à tous les partenaires. Deux
            // intégrateurs qui numérotent leurs commandes « 1024 » entrent donc
            // en collision : sans ce contrôle, le second recevrait l'identifiant
            // de la course du premier, puis pourrait la suivre et l'annuler.
            //
            // Ce n'est pas un cas tordu : « 1 », « 1000 », « ORDER-1 » sont les
            // premières commandes de tout site marchand.
            // ─────────────────────────────────────────────────────────────────
            if (existing.PartnerId != command.PartnerId)
            {
                return Result.Failure<Guid>(
                    Error.Conflict("delivery.reference_taken",
                        "Cette référence de commande est déjà utilisée. Employez une référence propre à votre système."));
            }

            return existing.Id.Value;
        }

        // ─────────────────────────────────────────────────────────────────────
        // STATUT ET QUOTA DU PARTENAIRE — VÉRIFIÉS ICI, PAS DANS LE FILTRE HTTP.
        //
        // Un filtre d'authentification établit QUI appelle ; il n'a pas à décider
        // ce que cet appelant a le droit de faire. Mettre le quota là-haut le
        // rendrait contournable par tout autre chemin d'entrée — une commande
        // interne, un futur consommateur de file, un test d'intégration.
        //
        // La règle appartient au métier : elle vaut quel que soit le transport.
        // ─────────────────────────────────────────────────────────────────────
        if (command.PartnerId is { } partnerId)
        {
            var guard = await CheckPartnerAsync(new PartnerId(partnerId), cancellationToken);
            if (guard.IsFailure)
            {
                return Result.Failure<Guid>(guard.Error);
            }
        }

        var pickup = BuildStop(command.Pickup);
        if (pickup.IsFailure)
        {
            return Result.Failure<Guid>(pickup.Error);
        }

        var dropoff = BuildStop(command.Dropoff);
        if (dropoff.IsFailure)
        {
            return Result.Failure<Guid>(dropoff.Error);
        }

        var package = DeliveryPackage.Create(
            command.Package.Description, command.Package.WeightKg,
            command.Package.IsFragile, command.Package.IsPerishable);
        if (package.IsFailure)
        {
            return Result.Failure<Guid>(package.Error);
        }

        var delivery = Domain.Deliveries.Delivery.Create(
            command.Reference, command.Source, command.Type,
            pickup.Value, dropoff.Value, package.Value, command.RequiredProof, command.PartnerId,
            command.ScheduledForUtc);
        if (delivery.IsFailure)
        {
            return Result.Failure<Guid>(delivery.Error);
        }

        // ─────────────────────────────────────────────────────────────────────
        // LA RECHERCHE DÉMARRE TOUT DE SUITE — SAUF POUR UNE COURSE PROGRAMMÉE.
        //
        // Pour une course immédiate, rien ne justifie de la laisser dormir : un
        // « Pending » qui persiste est une course que personne ne cherche à
        // pourvoir.
        //
        // Pour une course programmée, l'inverse est vrai. Elle reste « Pending »
        // jusqu'à l'ouverture de sa fenêtre, et c'est la boucle de dispatch qui
        // l'ouvre — voir IDeliveryRepository.ListScheduledDueAsync. Appeler
        // StartSearching ici échouerait d'ailleurs : l'agrégat refuse.
        // ─────────────────────────────────────────────────────────────────────
        if (delivery.Value.ScheduledForUtc is null)
        {
            var searching = delivery.Value.StartSearching();
            if (searching.IsFailure)
            {
                return Result.Failure<Guid>(searching.Error);
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // LE DEVIS EST CONSOMMÉ ICI, ET UNE SEULE FOIS.
        //
        // Il porte le prix convenu ; sans lui, la course n'a pas de montant et
        // rien ne pourra être facturé. La consommation refuse deux cas distincts,
        // et le message le dit : un devis EXPIRÉ se redemande, un devis DÉJÀ
        // UTILISÉ révèle un défaut dans l'intégration de l'appelant.
        // ─────────────────────────────────────────────────────────────────────
        if (command.QuoteId is not null)
        {
            var quoteId = DeliveryQuoteId.FromPublicId(command.QuoteId);
            if (quoteId is null)
            {
                return Result.Failure<Guid>(
                    Error.Validation("pricing.quote.malformed", "Référence de devis illisible (forme attendue : « qt_… »)."));
            }

            var quote = await _quotes.GetByIdAsync(quoteId.Value, cancellationToken);
            if (quote is null)
            {
                return Result.Failure<Guid>(
                    Error.NotFound("pricing.quote.not_found", "Devis introuvable."));
            }

            // Un devis émis pour un partenaire ne vaut que pour lui : sans ce
            // contrôle, un partenaire pourrait rejouer la référence d'un autre.
            if (quote.PartnerId != command.PartnerId)
            {
                return Result.Failure<Guid>(
                    Error.Forbidden("pricing.quote.foreign", "Ce devis n'appartient pas au demandeur."));
            }

            var consumed = quote.Consume(delivery.Value.Id.Value, DateTime.UtcNow);
            if (consumed.IsFailure)
            {
                return Result.Failure<Guid>(consumed.Error);
            }

            delivery.Value.AttachQuote(quote.Id.Value, quote.Total, quote.Currency);
        }

        await _repository.AddAsync(delivery.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return delivery.Value.Id.Value;
    }

    /// <summary>
    /// Le partenaire a-t-il le droit de créer une course, maintenant ?
    ///
    /// Deux refus distincts, et la distinction compte pour l'intégrateur :
    /// « suspendu » se règle avec le service commercial, « quota atteint » se
    /// règle en attendant demain ou en demandant un relèvement. Un message unique
    /// enverrait la moitié des partenaires frapper à la mauvaise porte.
    /// </summary>
    private async Task<Result> CheckPartnerAsync(PartnerId partnerId, CancellationToken cancellationToken)
    {
        var partner = await _partners.GetByIdAsync(partnerId, cancellationToken);
        if (partner is null)
        {
            return Result.Failure(Error.NotFound("partner.not_found", "Partenaire introuvable."));
        }

        if (!partner.CanCreateDeliveries)
        {
            return Result.Failure(
                Error.Forbidden("partner.not_active",
                    "Votre compte partenaire n'est pas actif. Contactez votre interlocuteur HBA."));
        }

        // Quota nul = illimité. Le contrôle est APPROXIMATIF sous forte
        // concurrence : deux requêtes simultanées peuvent lire le même compte et
        // passer toutes les deux. C'est assumé — un plafond anti-abus dépassé
        // d'une unité ne coûte rien, alors qu'un verrou sur le chemin de création
        // coûterait à chaque appel.
        if (partner.DailyQuota > 0)
        {
            var today = await _partners.CountDeliveriesTodayAsync(partnerId, cancellationToken);
            if (today >= partner.DailyQuota)
            {
                return Result.Failure(
                    Error.Forbidden("partner.quota_exceeded",
                        $"Quota quotidien atteint ({partner.DailyQuota} livraisons). "
                        + "Il se réinitialise à minuit UTC."));
            }
        }

        return Result.Success();
    }

    private static Result<DeliveryStop> BuildStop(DeliveryStopInput input)
    {
        Coordinates? position = null;

        // Les deux coordonnées vont ensemble : une seule des deux est traitée
        // comme aucune. Voir Coordinates.Create pour le refus du point (0, 0).
        if (input.Latitude is { } lat && input.Longitude is { } lon)
        {
            var coordinates = Coordinates.Create(lat, lon);
            if (coordinates.IsFailure)
            {
                return Result.Failure<DeliveryStop>(coordinates.Error);
            }

            position = coordinates.Value;
        }

        return DeliveryStop.Create(
            input.ContactName, input.Phone, input.Commune,
            input.Quartier, input.Landmark, input.Instructions, position);
    }
}
