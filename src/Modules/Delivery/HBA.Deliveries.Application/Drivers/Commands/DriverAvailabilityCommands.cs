using FluentValidation;
using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Application.Drivers.Commands;

/// <summary>Inscrit un livreur. Le compte reste à vérifier avant de pouvoir travailler.</summary>
public sealed record RegisterDriverCommand(
    Guid UserId, string FullName, string Phone, VehicleType Vehicle) : ICommand<Guid>;

/// <summary>Le livreur se met en ligne et devient joignable par le dispatch.</summary>
public sealed record GoOnlineCommand(Guid DriverId) : ICommand;

/// <summary>Le livreur se met hors ligne.</summary>
public sealed record GoOfflineCommand(Guid DriverId) : ICommand;

/// <summary>Position transmise par l'application HBA Driver.</summary>
public sealed record UpdateDriverLocationCommand(Guid DriverId, double Latitude, double Longitude) : ICommand;

internal sealed class RegisterDriverCommandValidator : AbstractValidator<RegisterDriverCommand>
{
    public RegisterDriverCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.FullName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Phone).NotEmpty();
    }
}

internal sealed class RegisterDriverCommandHandler : ICommandHandler<RegisterDriverCommand, Guid>
{
    private readonly IDriverRepository _drivers;
    private readonly IDeliveryUnitOfWork _unitOfWork;

    public RegisterDriverCommandHandler(IDriverRepository drivers, IDeliveryUnitOfWork unitOfWork)
    {
        _drivers = drivers;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(RegisterDriverCommand command, CancellationToken cancellationToken)
    {
        // Un compte utilisateur ne peut porter qu'un seul livreur. Sans ce contrôle,
        // un double appui à l'inscription crée deux livreurs pour la même personne,
        // qui recevrait alors deux propositions pour la même course.
        var existing = await _drivers.GetByUserIdAsync(command.UserId, cancellationToken);
        if (existing is not null)
        {
            return Result.Failure<Guid>(
                Error.Conflict("driver.already_registered", "Ce compte est déjà inscrit comme livreur."));
        }

        var driver = Driver.Register(command.UserId, command.FullName, command.Phone, command.Vehicle);
        if (driver.IsFailure)
        {
            return Result.Failure<Guid>(driver.Error);
        }

        await _drivers.AddAsync(driver.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return driver.Value.Id.Value;
    }
}

/// <summary>
/// Mise en ligne, mise hors ligne, position.
///
/// Ces trois opérations partagent la même contrainte : elles touchent le CACHE
/// autant que la base. Un livreur qui se déconnecte doit disparaître du cache
/// immédiatement, sinon le dispatch continue de le proposer et perd un délai
/// d'expiration à chaque course.
/// </summary>
internal sealed class DriverAvailabilityCommandHandler
    : ICommandHandler<GoOnlineCommand>, ICommandHandler<GoOfflineCommand>, ICommandHandler<UpdateDriverLocationCommand>
{
    /// <summary>
    /// Fréquence de recopie de la position en base. La position vit dans Redis ;
    /// on n'en persiste une que de loin en loin, uniquement pour survivre à un
    /// vidage du cache. Écrire à chaque relevé — toutes les cinq secondes et par
    /// livreur — saturerait le journal de transactions pour une donnée jetable.
    /// </summary>
    private static readonly TimeSpan PersistPositionEvery = TimeSpan.FromMinutes(5);

    private readonly IDriverRepository _drivers;
    private readonly IDriverLocationCache _locations;
    private readonly IDeliveryUnitOfWork _unitOfWork;

    public DriverAvailabilityCommandHandler(
        IDriverRepository drivers, IDriverLocationCache locations, IDeliveryUnitOfWork unitOfWork)
    {
        _drivers = drivers;
        _locations = locations;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(GoOnlineCommand command, CancellationToken cancellationToken)
    {
        var driver = await Load(command.DriverId, cancellationToken);
        if (driver is null)
        {
            return NotFound();
        }

        var result = driver.GoOnline();
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> Handle(GoOfflineCommand command, CancellationToken cancellationToken)
    {
        var driver = await Load(command.DriverId, cancellationToken);
        if (driver is null)
        {
            return NotFound();
        }

        var result = driver.GoOffline();
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Le retrait du cache vient APRÈS la persistance. Dans l'autre ordre, un
        // échec d'enregistrement laisserait un livreur invisible du dispatch tout
        // en restant « disponible » en base — introuvable et pourtant censé
        // travailler.
        await _locations.RemoveAsync(driver.Id, cancellationToken);
        return Result.Success();
    }

    public async Task<Result> Handle(UpdateDriverLocationCommand command, CancellationToken cancellationToken)
    {
        var position = Coordinates.Create(command.Latitude, command.Longitude);
        if (position.IsFailure)
        {
            return Result.Failure(position.Error);
        }

        var driver = await Load(command.DriverId, cancellationToken);
        if (driver is null)
        {
            return NotFound();
        }

        // Un livreur hors ligne qui continue d'émettre — application restée
        // ouverte — ne doit pas réapparaître dans le cache.
        if (!driver.CanReceiveOffers && driver.Availability is not DriverAvailability.Busy)
        {
            return Result.Success();
        }

        await _locations.SetAsync(driver.Id, position.Value, cancellationToken);

        var lastPersisted = driver.LastPositionAtUtc;
        if (lastPersisted is null || DateTime.UtcNow - lastPersisted > PersistPositionEvery)
        {
            driver.RecordPosition(position.Value);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }

    private Task<Driver?> Load(Guid driverId, CancellationToken cancellationToken)
        => _drivers.GetByIdAsync(new DriverId(driverId), cancellationToken);

    private static Result NotFound()
        => Result.Failure(Error.NotFound("driver.not_found", "Livreur introuvable."));
}
