using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Billing.Application.Abstractions;
using Marketplace.Modules.Billing.Domain.Commissions;

namespace Marketplace.Modules.Billing.Application.Commissions;

/// <summary>Crée une règle de commission (Global / Category / Seller).</summary>
public sealed record CreateCommissionRuleCommand(
    string Scope, Guid? TargetId, decimal Rate, decimal FixedFee, string Currency,
    decimal? MinFee, decimal? MaxFee, DateTime EffectiveFromUtc) : ICommand<Guid>;

/// <summary>Désactive une règle de commission.</summary>
public sealed record DeactivateCommissionRuleCommand(Guid RuleId) : ICommand;

public sealed class CreateCommissionRuleCommandValidator : AbstractValidator<CreateCommissionRuleCommand>
{
    public CreateCommissionRuleCommandValidator()
    {
        RuleFor(c => c.Scope).Must(v => v is "Global" or "Category" or "Seller").WithMessage("Scope invalide.");
        RuleFor(c => c.Rate).InclusiveBetween(0m, 1m);
        RuleFor(c => c.FixedFee).GreaterThanOrEqualTo(0m);
        RuleFor(c => c.Currency).NotEmpty().Length(3);
    }
}

internal sealed class CreateCommissionRuleCommandHandler : ICommandHandler<CreateCommissionRuleCommand, Guid>
{
    private readonly ICommissionRuleRepository _repository;
    private readonly IBillingUnitOfWork _unitOfWork;

    public CreateCommissionRuleCommandHandler(ICommissionRuleRepository repository, IBillingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateCommissionRuleCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<CommissionScope>(command.Scope, ignoreCase: true, out var scope))
        {
            return Result.Failure<Guid>(Error.Validation("billing.scope_invalid", "Périmètre de commission inconnu."));
        }

        var result = CommissionRule.Create(
            scope, command.TargetId, command.Rate, command.FixedFee, command.Currency,
            command.MinFee, command.MaxFee, command.EffectiveFromUtc);

        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _repository.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result.Value.Id.Value;
    }
}

internal sealed class DeactivateCommissionRuleCommandHandler : ICommandHandler<DeactivateCommissionRuleCommand>
{
    private readonly ICommissionRuleRepository _repository;
    private readonly IBillingUnitOfWork _unitOfWork;

    public DeactivateCommissionRuleCommandHandler(ICommissionRuleRepository repository, IBillingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeactivateCommissionRuleCommand command, CancellationToken cancellationToken)
    {
        var rule = await _repository.GetByIdAsync(new CommissionRuleId(command.RuleId), cancellationToken);
        if (rule is null)
        {
            return Result.Failure(Error.NotFound("billing.rule.not_found", "Règle introuvable."));
        }

        rule.Deactivate();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
