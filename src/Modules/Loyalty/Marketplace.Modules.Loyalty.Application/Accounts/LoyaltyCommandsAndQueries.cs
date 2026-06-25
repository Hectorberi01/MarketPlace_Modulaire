using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Loyalty.Application.Abstractions;
using Marketplace.Modules.Loyalty.Contracts;
using Marketplace.Modules.Loyalty.Domain.Accounts;

namespace Marketplace.Modules.Loyalty.Application.Accounts;

/// <summary>Dépense des points (cashback) depuis le compte de l'utilisateur.</summary>
public sealed record RedeemPointsCommand(Guid UserId, int Amount) : ICommand;

/// <summary>Récupère le compte de fidélité de l'utilisateur (vide si inexistant).</summary>
public sealed record GetMyLoyaltyQuery(Guid UserId) : IQuery<LoyaltyAccountSummary>;

public sealed class RedeemPointsCommandValidator : AbstractValidator<RedeemPointsCommand>
{
    public RedeemPointsCommandValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0);
    }
}

internal sealed class RedeemPointsCommandHandler : ICommandHandler<RedeemPointsCommand>
{
    private readonly ILoyaltyAccountRepository _repository;
    private readonly ILoyaltyUnitOfWork _unitOfWork;

    public RedeemPointsCommandHandler(ILoyaltyAccountRepository repository, ILoyaltyUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RedeemPointsCommand command, CancellationToken cancellationToken)
    {
        var account = await _repository.GetByUserAsync(command.UserId, cancellationToken);
        if (account is null)
        {
            return Result.Failure(Error.NotFound("loyalty.not_found", "Aucun compte de fidélité."));
        }

        var result = account.RedeemPoints(command.Amount);
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class GetMyLoyaltyQueryHandler : IQueryHandler<GetMyLoyaltyQuery, LoyaltyAccountSummary>
{
    private readonly ILoyaltyAccountRepository _repository;

    public GetMyLoyaltyQueryHandler(ILoyaltyAccountRepository repository) => _repository = repository;

    public async Task<Result<LoyaltyAccountSummary>> Handle(GetMyLoyaltyQuery query, CancellationToken cancellationToken)
    {
        var account = await _repository.GetByUserAsync(query.UserId, cancellationToken);
        if (account is null)
        {
            return new LoyaltyAccountSummary(Guid.Empty, query.UserId, 0, 0, LoyaltyTier.Bronze.ToString(), Array.Empty<PointTransactionSummary>());
        }

        var transactions = account.Transactions
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new PointTransactionSummary(t.Amount, t.Reason.ToString(), t.RelatedOrderId, t.CreatedAtUtc))
            .ToList();

        return new LoyaltyAccountSummary(
            account.Id.Value, account.UserId, account.PointsBalance, account.LifetimePoints, account.Tier.ToString(), transactions);
    }
}
