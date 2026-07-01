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

/// <summary>Modifie une règle de commission existante (le périmètre n'est pas modifiable).</summary>
public sealed record UpdateCommissionRuleCommand(
    Guid RuleId, decimal Rate, decimal FixedFee, string Currency,
    decimal? MinFee, decimal? MaxFee, DateTime EffectiveFromUtc) : ICommand;

/// <summary>Désactive une règle de commission.</summary>
public sealed record DeactivateCommissionRuleCommand(Guid RuleId) : ICommand;

/// <summary>Réactive une règle de commission désactivée.</summary>
public sealed record ReactivateCommissionRuleCommand(Guid RuleId) : ICommand;

/// <summary>Supprime définitivement une règle de commission.</summary>
public sealed record DeleteCommissionRuleCommand(Guid RuleId) : ICommand;

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

internal sealed class UpdateCommissionRuleCommandHandler : ICommandHandler<UpdateCommissionRuleCommand>
{
    private readonly ICommissionRuleRepository _repository;
    private readonly IBillingUnitOfWork _unitOfWork;

    public UpdateCommissionRuleCommandHandler(ICommissionRuleRepository repository, IBillingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateCommissionRuleCommand command, CancellationToken cancellationToken)
    {
        var rule = await _repository.GetByIdAsync(new CommissionRuleId(command.RuleId), cancellationToken);
        if (rule is null)
        {
            return Result.Failure(Error.NotFound("billing.rule.not_found", "Règle introuvable."));
        }

        var result = rule.Update(command.Rate, command.FixedFee, command.Currency, command.MinFee, command.MaxFee, command.EffectiveFromUtc);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class ReactivateCommissionRuleCommandHandler : ICommandHandler<ReactivateCommissionRuleCommand>
{
    private readonly ICommissionRuleRepository _repository;
    private readonly IBillingUnitOfWork _unitOfWork;

    public ReactivateCommissionRuleCommandHandler(ICommissionRuleRepository repository, IBillingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ReactivateCommissionRuleCommand command, CancellationToken cancellationToken)
    {
        var rule = await _repository.GetByIdAsync(new CommissionRuleId(command.RuleId), cancellationToken);
        if (rule is null)
        {
            return Result.Failure(Error.NotFound("billing.rule.not_found", "Règle introuvable."));
        }

        rule.Reactivate();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class DeleteCommissionRuleCommandHandler : ICommandHandler<DeleteCommissionRuleCommand>
{
    private readonly ICommissionRuleRepository _repository;
    private readonly IBillingUnitOfWork _unitOfWork;

    public DeleteCommissionRuleCommandHandler(ICommissionRuleRepository repository, IBillingUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteCommissionRuleCommand command, CancellationToken cancellationToken)
    {
        var rule = await _repository.GetByIdAsync(new CommissionRuleId(command.RuleId), cancellationToken);
        if (rule is null)
        {
            return Result.Failure(Error.NotFound("billing.rule.not_found", "Règle introuvable."));
        }

        _repository.Remove(rule);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
