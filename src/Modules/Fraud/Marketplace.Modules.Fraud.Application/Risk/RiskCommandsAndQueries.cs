using System.Text.Json;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Fraud.Contracts;
using Marketplace.Modules.Fraud.Domain.Risk;

namespace Marketplace.Modules.Fraud.Application.Risk;

/// <summary>Unit of Work propre au module Fraud.</summary>
public interface IFraudUnitOfWork : IUnitOfWork
{
}

/// <summary>Évalue le risque d'un sujet à partir de signaux pondérés (vélocité, géoloc…).</summary>
public sealed record AssessRiskCommand(string SubjectType, Guid SubjectId, IReadOnlyDictionary<string, double> Signals) : ICommand<Guid>;

/// <summary>Récupère la dernière évaluation de risque d'un sujet.</summary>
public sealed record GetRiskAssessmentQuery(string SubjectType, Guid SubjectId) : IQuery<RiskAssessmentSummary>;

internal static class RiskMapper
{
    public static RiskAssessmentSummary ToSummary(RiskAssessment a) => new(
        a.Id.Value, a.SubjectType.ToString(), a.SubjectId, a.Score, a.Signals, a.Decision.ToString(), a.EvaluatedAtUtc);
}

internal sealed class AssessRiskCommandHandler : ICommandHandler<AssessRiskCommand, Guid>
{
    private readonly IRiskAssessmentRepository _repository;
    private readonly IFraudUnitOfWork _unitOfWork;

    public AssessRiskCommandHandler(IRiskAssessmentRepository repository, IFraudUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(AssessRiskCommand command, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<RiskSubjectType>(command.SubjectType, ignoreCase: true, out var subjectType))
        {
            return Result.Failure<Guid>(Error.Validation("fraud.subject_type_invalid", "Type de sujet inconnu."));
        }

        var signals = command.Signals ?? new Dictionary<string, double>();
        var (score, decision) = RiskScorer.Evaluate(signals);
        var signalsJson = JsonSerializer.Serialize(signals);

        var result = RiskAssessment.Create(subjectType, command.SubjectId, score, signalsJson, decision);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _repository.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return result.Value.Id.Value;
    }
}

internal sealed class GetRiskAssessmentQueryHandler : IQueryHandler<GetRiskAssessmentQuery, RiskAssessmentSummary>
{
    private readonly IRiskAssessmentRepository _repository;

    public GetRiskAssessmentQueryHandler(IRiskAssessmentRepository repository) => _repository = repository;

    public async Task<Result<RiskAssessmentSummary>> Handle(GetRiskAssessmentQuery query, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<RiskSubjectType>(query.SubjectType, ignoreCase: true, out var subjectType))
        {
            return Error.Validation("fraud.subject_type_invalid", "Type de sujet inconnu.");
        }

        var assessment = await _repository.GetLatestAsync(subjectType, query.SubjectId, cancellationToken);
        return assessment is null
            ? Error.NotFound("fraud.not_found", "Aucune évaluation pour ce sujet.")
            : RiskMapper.ToSummary(assessment);
    }
}
