using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Pricing.Application.Promotions.Commands.CreatePromotion;

/// <summary>Crée une promotion (brouillon) : périmètre, financeur, conditions, période.</summary>
public sealed record CreatePromotionCommand(
    string OwnerType,
    Guid OwnerId,
    string FundedBy,
    string Type,
    decimal Value,
    string ScopeType,
    IReadOnlyList<Guid> Targets,
    decimal? MinAmount,
    int? MinQuantity,
    bool FirstOrderOnly,
    string? Code,
    DateTime StartUtc,
    DateTime EndUtc,
    int UsageLimit,
    /// <summary>Plafond par acheteur. 0 = illimité. Sans lui, un coupon « une fois par
    /// personne » n'existe pas : le même acheteur épuise tout le plafond global.</summary>
    int PerUserLimit = 0) : ICommand<Guid>;
