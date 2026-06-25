using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Pricing.Application.Abstractions;
using Marketplace.Modules.Pricing.Domain.Promotions;

namespace Marketplace.Modules.Pricing.Application.Promotions.Commands;

/// <summary>Active une promotion (Draft/Paused -> Active).</summary>
public sealed record ActivatePromotionCommand(Guid PromotionId) : ICommand;

/// <summary>Met une promotion en pause.</summary>
public sealed record PausePromotionCommand(Guid PromotionId) : ICommand;

/// <summary>Expire (clôt) une promotion.</summary>
public sealed record ExpirePromotionCommand(Guid PromotionId) : ICommand;

internal sealed class ActivatePromotionCommandHandler : ICommandHandler<ActivatePromotionCommand>
{
    private readonly IPromotionRepository _repository;
    private readonly IPricingUnitOfWork _unitOfWork;

    public ActivatePromotionCommandHandler(IPromotionRepository repository, IPricingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ActivatePromotionCommand command, CancellationToken cancellationToken)
    {
        var promotion = await _repository.GetByIdAsync(new PromotionId(command.PromotionId), cancellationToken);
        if (promotion is null)
        {
            return Result.Failure(Error.NotFound("pricing.promotion.not_found", "Promotion introuvable."));
        }

        var result = promotion.Activate();
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class PausePromotionCommandHandler : ICommandHandler<PausePromotionCommand>
{
    private readonly IPromotionRepository _repository;
    private readonly IPricingUnitOfWork _unitOfWork;

    public PausePromotionCommandHandler(IPromotionRepository repository, IPricingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(PausePromotionCommand command, CancellationToken cancellationToken)
    {
        var promotion = await _repository.GetByIdAsync(new PromotionId(command.PromotionId), cancellationToken);
        if (promotion is null)
        {
            return Result.Failure(Error.NotFound("pricing.promotion.not_found", "Promotion introuvable."));
        }

        promotion.Pause();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class ExpirePromotionCommandHandler : ICommandHandler<ExpirePromotionCommand>
{
    private readonly IPromotionRepository _repository;
    private readonly IPricingUnitOfWork _unitOfWork;

    public ExpirePromotionCommandHandler(IPromotionRepository repository, IPricingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ExpirePromotionCommand command, CancellationToken cancellationToken)
    {
        var promotion = await _repository.GetByIdAsync(new PromotionId(command.PromotionId), cancellationToken);
        if (promotion is null)
        {
            return Result.Failure(Error.NotFound("pricing.promotion.not_found", "Promotion introuvable."));
        }

        promotion.Expire();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
