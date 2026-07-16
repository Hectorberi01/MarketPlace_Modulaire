using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Pricing.Application.Abstractions;
using Marketplace.Modules.Pricing.Domain.Promotions;

namespace Marketplace.Modules.Pricing.Application.Promotions.Commands.CreatePromotion;

internal sealed class CreatePromotionCommandHandler : ICommandHandler<CreatePromotionCommand, Guid>
{
    private readonly IPromotionRepository _repository;
    private readonly IPricingUnitOfWork _unitOfWork;

    public CreatePromotionCommandHandler(IPromotionRepository repository, IPricingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreatePromotionCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<PromotionOwnerType>(command.OwnerType, ignoreCase: true, out var ownerType)
            || !Enum.TryParse<FundedBy>(command.FundedBy, ignoreCase: true, out var fundedBy)
            || !Enum.TryParse<PromotionType>(command.Type, ignoreCase: true, out var type)
            || !Enum.TryParse<PromotionScopeType>(command.ScopeType, ignoreCase: true, out var scopeType))
        {
            return Error.Validation("pricing.promotion.enum_invalid", "Valeur d'énumération invalide.");
        }

        var periodResult = DateRange.Create(command.StartUtc, command.EndUtc);
        if (periodResult.IsFailure)
        {
            return Result.Failure<Guid>(periodResult.Error);
        }

        var conditions = new PromotionConditions(command.MinAmount, command.MinQuantity, command.FirstOrderOnly);

        var result = Promotion.Create(
            ownerType, command.OwnerId, fundedBy, type, command.Value, scopeType,
            command.Targets, conditions, command.Code, periodResult.Value, command.UsageLimit, command.PerUserLimit);

        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _repository.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result.Value.Id.Value;
    }
}
