using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Shipping.Application.Abstractions;
using Marketplace.Modules.Shipping.Domain.Zones;

namespace Marketplace.Modules.Shipping.Application.Zones;

/// <summary>Crée une zone de livraison (admin). Renvoie son id.</summary>
public sealed record CreateShippingZoneCommand(string Code, string Name, bool IsFallback) : ICommand<Guid>;

/// <summary>Renomme une zone et remplace la liste de ses communes.</summary>
public sealed record UpdateShippingZoneCommand(Guid ZoneId, string Name, IReadOnlyList<string>? CommuneCodes) : ICommand;

/// <summary>Fixe (ou remplace) le tarif d'un service dans une zone.</summary>
public sealed record SetShippingRateCommand(
    Guid ZoneId, string ServiceCode, string Label, string? Eta, decimal Amount, string? Currency) : ICommand;

/// <summary>Retire le tarif d'un service dans une zone.</summary>
public sealed record RemoveShippingRateCommand(Guid ZoneId, string ServiceCode) : ICommand;

/// <summary>Active ou désactive une zone. La zone de repli ne peut pas être désactivée.</summary>
public sealed record SetShippingZoneActiveCommand(Guid ZoneId, bool Active) : ICommand;

// ─────────────────────────────────────────────────────────────────────────────────
// VALIDATION
// ─────────────────────────────────────────────────────────────────────────────────

internal sealed class CreateShippingZoneCommandValidator : AbstractValidator<CreateShippingZoneCommand>
{
    public CreateShippingZoneCommandValidator()
    {
        RuleFor(c => c.Code).NotEmpty().MaximumLength(ShippingZone.MaxCode);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(ShippingZone.MaxName);
    }
}

internal sealed class SetShippingRateCommandValidator : AbstractValidator<SetShippingRateCommand>
{
    public SetShippingRateCommandValidator()
    {
        RuleFor(c => c.ServiceCode).NotEmpty().MaximumLength(ShippingRate.MaxServiceCode);
        RuleFor(c => c.Label).NotEmpty().MaximumLength(ShippingRate.MaxLabel);

        // Zéro est ACCEPTÉ : la livraison offerte est une opération commerciale
        // courante, et l'interdire obligerait à la simuler par une promotion, donc à
        // faire mentir la grille.
        RuleFor(c => c.Amount).GreaterThanOrEqualTo(0m);
    }
}

// ─────────────────────────────────────────────────────────────────────────────────
// HANDLERS
// ─────────────────────────────────────────────────────────────────────────────────

internal sealed class CreateShippingZoneCommandHandler : ICommandHandler<CreateShippingZoneCommand, Guid>
{
    private readonly IShippingZoneRepository _zones;
    private readonly IShippingUnitOfWork _unitOfWork;

    public CreateShippingZoneCommandHandler(IShippingZoneRepository zones, IShippingUnitOfWork unitOfWork)
    {
        _zones = zones;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateShippingZoneCommand command, CancellationToken cancellationToken)
    {
        var created = ShippingZone.Create(command.Code, command.Name, command.IsFallback);
        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        var zone = created.Value;

        if (await _zones.GetByCodeAsync(zone.Code, cancellationToken) is not null)
        {
            return Result.Failure<Guid>(Error.Conflict(
                "shipping.zone.code_taken", $"Une zone avec le code « {zone.Code} » existe déjà."));
        }

        // ─────────────────────────────────────────────────────────────────────────
        // UN SEUL REPLI, VÉRIFIÉ ICI AUSSI.
        //
        // L'index unique partiel en base est le garde-fou qui ne ment jamais ; ce
        // contrôle-ci existe pour que l'administrateur lise « il y a déjà une zone
        // nationale » plutôt qu'une violation de contrainte traduite en 500.
        // Redondant, et c'est le but : le message est ce qui change.
        // ─────────────────────────────────────────────────────────────────────────
        if (zone.IsFallback)
        {
            var existing = await _zones.ListAsync(cancellationToken);
            if (existing.Any(z => z.IsFallback && z.IsActive))
            {
                return Result.Failure<Guid>(Error.Conflict(
                    "shipping.zone.fallback_exists",
                    "Une zone de repli active existe déjà : modifiez-la plutôt que d'en créer une seconde."));
            }
        }

        await _zones.AddAsync(zone, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(zone.Id.Value);
    }
}

internal sealed class UpdateShippingZoneCommandHandler : ICommandHandler<UpdateShippingZoneCommand>
{
    private readonly IShippingZoneRepository _zones;
    private readonly IShippingUnitOfWork _unitOfWork;

    public UpdateShippingZoneCommandHandler(IShippingZoneRepository zones, IShippingUnitOfWork unitOfWork)
    {
        _zones = zones;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateShippingZoneCommand command, CancellationToken cancellationToken)
    {
        var zone = await _zones.GetByIdAsync(new ShippingZoneId(command.ZoneId), cancellationToken);
        if (zone is null)
        {
            return Result.Failure(ZoneNotFound);
        }

        var renamed = zone.Rename(command.Name);
        if (renamed.IsFailure)
        {
            return renamed;
        }

        // Le remplacement des communes est REFUSÉ EN BLOC si l'une d'elles est
        // inconnue : appliquer les valides et ignorer le reste laisserait
        // l'administrateur croire que sa zone couvre une commune qu'elle a rejetée.
        var communes = zone.SetCommunes(command.CommuneCodes);
        if (communes.IsFailure)
        {
            return communes;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    internal static Error ZoneNotFound
        => Error.NotFound("shipping.zone.not_found", "Zone de livraison introuvable.");
}

internal sealed class SetShippingRateCommandHandler : ICommandHandler<SetShippingRateCommand>
{
    private readonly IShippingZoneRepository _zones;
    private readonly IShippingUnitOfWork _unitOfWork;

    public SetShippingRateCommandHandler(IShippingZoneRepository zones, IShippingUnitOfWork unitOfWork)
    {
        _zones = zones;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SetShippingRateCommand command, CancellationToken cancellationToken)
    {
        var zone = await _zones.GetByIdAsync(new ShippingZoneId(command.ZoneId), cancellationToken);
        if (zone is null)
        {
            return Result.Failure(UpdateShippingZoneCommandHandler.ZoneNotFound);
        }

        var result = zone.SetRate(command.ServiceCode, command.Label, command.Eta, command.Amount, command.Currency);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class RemoveShippingRateCommandHandler : ICommandHandler<RemoveShippingRateCommand>
{
    private readonly IShippingZoneRepository _zones;
    private readonly IShippingUnitOfWork _unitOfWork;

    public RemoveShippingRateCommandHandler(IShippingZoneRepository zones, IShippingUnitOfWork unitOfWork)
    {
        _zones = zones;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RemoveShippingRateCommand command, CancellationToken cancellationToken)
    {
        var zone = await _zones.GetByIdAsync(new ShippingZoneId(command.ZoneId), cancellationToken);
        if (zone is null)
        {
            return Result.Failure(UpdateShippingZoneCommandHandler.ZoneNotFound);
        }

        var result = zone.RemoveRate(command.ServiceCode);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class SetShippingZoneActiveCommandHandler : ICommandHandler<SetShippingZoneActiveCommand>
{
    private readonly IShippingZoneRepository _zones;
    private readonly IShippingUnitOfWork _unitOfWork;

    public SetShippingZoneActiveCommandHandler(IShippingZoneRepository zones, IShippingUnitOfWork unitOfWork)
    {
        _zones = zones;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(SetShippingZoneActiveCommand command, CancellationToken cancellationToken)
    {
        var zone = await _zones.GetByIdAsync(new ShippingZoneId(command.ZoneId), cancellationToken);
        if (zone is null)
        {
            return Result.Failure(UpdateShippingZoneCommandHandler.ZoneNotFound);
        }

        if (command.Active)
        {
            zone.Activate();
        }
        else
        {
            // L'agrégat refuse de désactiver le repli : c'est lui qui garantit qu'aucune
            // commune ne reste sans tarif.
            var deactivated = zone.Deactivate();
            if (deactivated.IsFailure)
            {
                return deactivated;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
