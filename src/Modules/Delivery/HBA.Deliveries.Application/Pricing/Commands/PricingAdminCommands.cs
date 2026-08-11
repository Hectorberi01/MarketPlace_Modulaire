using FluentValidation;
using HBA.Deliveries.Application.Abstractions;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using HBA.Deliveries.Domain.Pricing;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Application.Pricing.Commands;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'ADMINISTRATION DE LA TARIFICATION.
///
/// Jusqu'ici, seul l'amorçage écrivait dans les tables de zones et de grilles :
/// un tarif ne s'ajustait qu'en base, à la main. C'est particulièrement gênant
/// pour le FACTEUR ROUTIER, qui est CONÇU pour se calibrer — on compare la
/// distance réelle des courses livrées à l'orthodromie, et on ajuste.
///
/// ⚠️ ON NE MODIFIE PAS UNE GRILLE. ON EN PUBLIE UNE NOUVELLE.
///
/// Il n'y a volontairement aucune commande « changer le prix au kilomètre ».
/// Chaque devis émis conserve l'identifiant ET la version de la grille qui l'a
/// produit, précisément pour qu'un montant contesté puisse être refait six mois
/// plus tard. Modifier une grille en place rendrait tous ces devis irreproduisibles
/// — et un devis qu'on ne peut pas refaire n'est pas opposable, c'est une
/// affirmation.
///
/// Le geste correct est donc : publier la nouvelle grille, désactiver l'ancienne.
/// Les devis déjà émis continuent de pointer vers une grille inactive, et c'est
/// exactement ce qu'on veut.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="Boundary">
/// Sommets du contour, dans l'ordre. Au moins trois — un polygone à deux points
/// est un segment, et ne contient rien.
/// </param>
public sealed record CreateDeliveryZoneCommand(
    string? Name,
    string? City,
    IReadOnlyList<GeoPointInput> Boundary,
    decimal BaseFee = 0m,
    decimal ExtraFee = 0m,
    double RoadFactor = DeliveryZone.DefaultRoadFactor,
    int Priority = 0) : ICommand<Guid>;

/// <summary>Un sommet du contour d'une zone.</summary>
public sealed record GeoPointInput(double Latitude, double Longitude);

/// <summary>
/// Ajuste le facteur routier d'une zone. C'est L'OPÉRATION DE CALIBRAGE : elle a
/// vocation à être répétée à mesure que de vraies courses sont livrées.
/// </summary>
public sealed record RecalibrateZoneCommand(Guid ZoneId, double RoadFactor) : ICommand;

/// <summary>Met à jour les suppléments de départ et d'arrivée d'une zone.</summary>
public sealed record UpdateZoneFeesCommand(Guid ZoneId, decimal BaseFee, decimal ExtraFee) : ICommand;

/// <summary>Active ou désactive une zone. Une zone inactive ne contient plus aucun point.</summary>
public sealed record SetZoneActiveCommand(Guid ZoneId, bool Active) : ICommand;

/// <summary>Un palier de poids d'une grille.</summary>
public sealed record WeightTierInput(decimal FromKg, decimal? ToKg, decimal Surcharge);

/// <summary>
/// Publie une grille tarifaire.
///
/// <c>Version</c> n'est pas décoratif : il est figé dans chaque devis émis sous
/// cette grille. Publier une révision d'un tarif existant, c'est créer une
/// nouvelle grille avec un numéro supérieur, puis désactiver l'ancienne.
/// </summary>
public sealed record CreatePricingRuleCommand(
    string? Name,
    decimal BasePrice,
    decimal PricePerKm,
    decimal MinimumPrice = 0m,
    double? MaximumDistanceKm = null,
    DeliveryType? DeliveryType = null,
    VehicleType? VehicleType = null,
    decimal UrgencySurcharge = 0m,
    DateTime? ValidFromUtc = null,
    DateTime? ValidUntilUtc = null,
    int Version = 1,
    IReadOnlyList<WeightTierInput>? WeightTiers = null) : ICommand<Guid>;

/// <summary>Retire une grille du calcul. Les devis déjà émis sous cette grille restent valides.</summary>
public sealed record DeactivatePricingRuleCommand(Guid PricingRuleId) : ICommand;

internal sealed class CreateDeliveryZoneCommandValidator : AbstractValidator<CreateDeliveryZoneCommand>
{
    public CreateDeliveryZoneCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(120);
        RuleFor(c => c.Boundary).NotNull().Must(b => b is { Count: >= 3 })
            .WithMessage("Un contour de zone demande au moins trois sommets.");
    }
}

internal sealed class CreatePricingRuleCommandValidator : AbstractValidator<CreatePricingRuleCommand>
{
    public CreatePricingRuleCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(120);
        RuleFor(c => c.Version).GreaterThan(0);
    }
}

internal sealed class PricingAdminCommandHandler
    : ICommandHandler<CreateDeliveryZoneCommand, Guid>,
      ICommandHandler<RecalibrateZoneCommand>,
      ICommandHandler<UpdateZoneFeesCommand>,
      ICommandHandler<SetZoneActiveCommand>,
      ICommandHandler<CreatePricingRuleCommand, Guid>,
      ICommandHandler<DeactivatePricingRuleCommand>
{
    private readonly IDeliveryZoneRepository _zones;
    private readonly IPricingRuleRepository _rules;
    private readonly IDeliveryUnitOfWork _unitOfWork;

    public PricingAdminCommandHandler(
        IDeliveryZoneRepository zones, IPricingRuleRepository rules, IDeliveryUnitOfWork unitOfWork)
    {
        _zones = zones;
        _rules = rules;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateDeliveryZoneCommand command, CancellationToken ct)
    {
        var boundary = GeoPolygon.Create(
            command.Boundary.Select(p => new GeoPoint(p.Latitude, p.Longitude)));

        if (boundary.IsFailure)
        {
            return Result.Failure<Guid>(boundary.Error);
        }

        var zone = DeliveryZone.Create(
            command.Name, command.City, boundary.Value,
            command.BaseFee, command.ExtraFee, command.RoadFactor, command.Priority);

        if (zone.IsFailure)
        {
            return Result.Failure<Guid>(zone.Error);
        }

        // ─────────────────────────────────────────────────────────────────────
        // DEUX ZONES DE MÊME PRIORITÉ QUI SE RECOUVRENT SONT AMBIGUËS.
        //
        // Le recouvrement lui-même est admis — deux quartiers tracés à la main se
        // chevauchent toujours. Mais à priorité égale, la zone retenue est
        // départagée par un GUID : deux installations, ou deux appels après une
        // restauration de base, factureraient différemment la même course sans
        // qu'aucune explication ne soit possible.
        //
        // On refuse donc la priorité en doublon. C'est plus strict que nécessaire
        // dans le cas où les zones ne se touchent pas — et c'est un prix modeste
        // face à un écart de facturation inexplicable.
        // ─────────────────────────────────────────────────────────────────────
        var existantes = await _zones.ListActiveAsync(ct);
        if (existantes.Any(z => z.Priority == command.Priority))
        {
            return Result.Failure<Guid>(Error.Conflict(
                "pricing.zone.priority_taken",
                $"La priorité {command.Priority} est déjà prise. Les priorités doivent être distinctes : "
                + "c'est ce qui rend le choix de zone reproductible quand deux zones se recouvrent."));
        }

        await _zones.AddAsync(zone.Value, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return zone.Value.Id.Value;
    }

    public Task<Result> Handle(RecalibrateZoneCommand c, CancellationToken ct)
        => MutateZoneAsync(c.ZoneId, z => z.Recalibrate(c.RoadFactor), ct);

    public Task<Result> Handle(UpdateZoneFeesCommand c, CancellationToken ct)
        => MutateZoneAsync(c.ZoneId, z => z.UpdateFees(c.BaseFee, c.ExtraFee), ct);

    public Task<Result> Handle(SetZoneActiveCommand c, CancellationToken ct)
        => MutateZoneAsync(c.ZoneId, z =>
        {
            if (c.Active)
            {
                z.Activate();
            }
            else
            {
                z.Deactivate();
            }

            return Result.Success();
        }, ct);

    public async Task<Result<Guid>> Handle(CreatePricingRuleCommand command, CancellationToken ct)
    {
        var rule = PricingRule.Create(
            command.Name, command.BasePrice, command.PricePerKm, command.MinimumPrice,
            command.MaximumDistanceKm, command.DeliveryType, command.VehicleType,
            command.UrgencySurcharge, command.ValidFromUtc, command.ValidUntilUtc, command.Version);

        if (rule.IsFailure)
        {
            return Result.Failure<Guid>(rule.Error);
        }

        // Les paliers sont ajoutés UN À UN et chaque refus interrompt : un
        // chevauchement rendrait la surcharge dépendante de l'ordre de lecture,
        // donc le devis non reproductible. Mieux vaut refuser la grille entière
        // que la publier à moitié.
        foreach (var tier in command.WeightTiers ?? [])
        {
            var added = rule.Value.AddWeightTier(tier.FromKg, tier.ToKg, tier.Surcharge);
            if (added.IsFailure)
            {
                return Result.Failure<Guid>(added.Error);
            }
        }

        await _rules.AddAsync(rule.Value, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return rule.Value.Id.Value;
    }

    public async Task<Result> Handle(DeactivatePricingRuleCommand command, CancellationToken ct)
    {
        var rule = await _rules.GetByIdAsync(new PricingRuleId(command.PricingRuleId), ct);
        if (rule is null)
        {
            return Result.Failure(Error.NotFound("pricing.rule.not_found", "Grille tarifaire introuvable."));
        }

        rule.Deactivate();
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    private async Task<Result> MutateZoneAsync(
        Guid zoneId, Func<DeliveryZone, Result> mutate, CancellationToken ct)
    {
        var zone = await _zones.GetByIdAsync(new DeliveryZoneId(zoneId), ct);
        if (zone is null)
        {
            return Result.Failure(Error.NotFound("pricing.zone.not_found", "Zone introuvable."));
        }

        var result = mutate(zone);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
