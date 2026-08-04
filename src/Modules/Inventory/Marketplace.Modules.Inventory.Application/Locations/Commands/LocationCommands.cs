using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Inventory.Application.Abstractions;
using Marketplace.Modules.Inventory.Domain.Locations;
using Marketplace.Modules.Inventory.Domain.Stock;

namespace Marketplace.Modules.Inventory.Application.Locations.Commands;

// ---- Création d'un lieu d'expédition --------------------------------------

/// <summary>Crée un lieu d'expédition (adresse vendeur FBS ou entrepôt plateforme FBP).</summary>
public sealed record CreateFulfillmentLocationCommand(
    string Type,
    Guid? OwnerId,
    string? Commune,
    string? Quartier,
    string? Landmark,
    string? Line,
    double? Latitude,
    double? Longitude) : ICommand<Guid>;

public sealed class CreateFulfillmentLocationCommandValidator : AbstractValidator<CreateFulfillmentLocationCommand>
{
    public CreateFulfillmentLocationCommandValidator()
    {
        RuleFor(c => c.Type).Must(t => t is "SellerAddress" or "PlatformWarehouse")
            .WithMessage("Type de lieu invalide (SellerAddress, PlatformWarehouse).");
        // Commune et repère sont validés par le VO Address, qui sait résoudre un code
        // comme un libellé et rend des messages métier. Les dupliquer ici en règles de
        // longueur ne ferait que produire deux messages différents pour la même erreur.
        RuleFor(c => c.Line).MaximumLength(500);
        RuleFor(c => c.Quartier).MaximumLength(120);
        RuleFor(c => c.Landmark).MaximumLength(200);
    }
}

internal sealed class CreateFulfillmentLocationCommandHandler : ICommandHandler<CreateFulfillmentLocationCommand, Guid>
{
    private readonly IFulfillmentLocationRepository _repository;
    private readonly IInventoryUnitOfWork _unitOfWork;

    public CreateFulfillmentLocationCommandHandler(IFulfillmentLocationRepository repository, IInventoryUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateFulfillmentLocationCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<FulfillmentLocationType>(command.Type, ignoreCase: true, out var type))
        {
            return Error.Validation("inventory.location.type_invalid", "Type de lieu invalide.");
        }

        var addressResult = Address.Create(
            command.Commune, command.Quartier, command.Landmark, command.Line,
            command.Latitude, command.Longitude);
        if (addressResult.IsFailure)
        {
            return Result.Failure<Guid>(addressResult.Error);
        }

        var result = FulfillmentLocation.Create(type, command.OwnerId, addressResult.Value);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _repository.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result.Value.Id.Value;
    }
}

// ---- Mise à jour d'adresse ------------------------------------------------

/// <summary>Met à jour l'adresse d'un lieu d'expédition.</summary>
public sealed record UpdateLocationAddressCommand(
    Guid LocationId,
    string? Commune,
    string? Quartier,
    string? Landmark,
    string? Line,
    double? Latitude,
    double? Longitude) : ICommand;

internal sealed class UpdateLocationAddressCommandHandler : ICommandHandler<UpdateLocationAddressCommand>
{
    private readonly IFulfillmentLocationRepository _repository;
    private readonly IInventoryUnitOfWork _unitOfWork;

    public UpdateLocationAddressCommandHandler(IFulfillmentLocationRepository repository, IInventoryUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateLocationAddressCommand command, CancellationToken cancellationToken)
    {
        var location = await _repository.GetByIdAsync(new FulfillmentLocationId(command.LocationId), cancellationToken);
        if (location is null)
        {
            return Result.Failure(Error.NotFound("inventory.location.not_found", "Lieu d'expédition introuvable."));
        }

        var addressResult = Address.Create(
            command.Commune, command.Quartier, command.Landmark, command.Line,
            command.Latitude, command.Longitude);
        if (addressResult.IsFailure)
        {
            return Result.Failure(addressResult.Error);
        }

        location.UpdateAddress(addressResult.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

// ---- Suppression d'un lieu -------------------------------------------------

/// <summary>
/// Supprime un lieu d'expédition. <see cref="OwnerId"/> optionnel : s'il est fourni
/// (mutation vendeur), la suppression n'aboutit que si le lieu appartient bien à ce
/// propriétaire (anti-IDOR). Null pour le back-office admin (aucun filtre).
/// </summary>
public sealed record DeleteFulfillmentLocationCommand(Guid LocationId, Guid? OwnerId = null) : ICommand;

internal sealed class DeleteFulfillmentLocationCommandHandler : ICommandHandler<DeleteFulfillmentLocationCommand>
{
    private readonly IFulfillmentLocationRepository _repository;
    private readonly IInventoryItemRepository _items;
    private readonly IInventoryUnitOfWork _unitOfWork;

    public DeleteFulfillmentLocationCommandHandler(
        IFulfillmentLocationRepository repository,
        IInventoryItemRepository items,
        IInventoryUnitOfWork unitOfWork)
    {
        _repository = repository;
        _items = items;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteFulfillmentLocationCommand command, CancellationToken cancellationToken)
    {
        var location = await _repository.GetByIdAsync(new FulfillmentLocationId(command.LocationId), cancellationToken);
        if (location is null)
        {
            return Result.Failure(Error.NotFound("inventory.location.not_found", "Lieu d'expédition introuvable."));
        }

        // Scoping vendeur : on ne révèle pas la différence « inexistant » / « pas le vôtre »
        // (même NotFound), pour ne pas divulguer les ids d'autres boutiques.
        if (command.OwnerId is { } ownerId && location.OwnerId != ownerId)
        {
            return Result.Failure(Error.NotFound("inventory.location.not_found", "Lieu d'expédition introuvable."));
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ON NE SUPPRIME PAS UN LIEU QUI PORTE ENCORE DU STOCK.
        //
        // Rien n'empêchait cette suppression : ni contrôle ici, ni clé étrangère —
        // `InventoryItemConfiguration` déclare `LocationId` comme une simple colonne,
        // sans `HasOne` vers les lieux. Les articles survivaient donc au lieu, rattachés
        // à un identifiant qui ne désignait plus rien.
        //
        // Ils ne réapparaissaient nulle part : toute lecture de stock passe par les
        // lieux du vendeur, si bien que ces articles disparaissaient des écrans SANS
        // avertissement, tandis que les offres pointant ce lieu restaient actives. Le
        // stock semblait s'être évaporé.
        //
        // Le contrôle est posé DANS LE DOMAINE, pas dans l'interface : c'est une règle
        // d'intégrité, pas une commodité d'affichage. Elle vaut donc pour le BFF
        // vendeur, pour l'admin, et pour tout futur appelant.
        // ─────────────────────────────────────────────────────────────────────────
        var attached = await _items.ListByLocationsAsync(new[] { command.LocationId }, cancellationToken);
        if (attached.Count > 0)
        {
            return Result.Failure(Error.Conflict(
                "inventory.location.has_stock",
                $"Ce lieu porte encore {attached.Count} référence(s) en stock. "
                + "Transférez-les ou cessez de les suivre avant de le supprimer."));
        }

        _repository.Remove(location);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
