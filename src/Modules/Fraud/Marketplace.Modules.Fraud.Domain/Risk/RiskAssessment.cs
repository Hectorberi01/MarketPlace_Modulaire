using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Fraud.Domain.Risk;

/// <summary>Identité forte d'une évaluation de risque.</summary>
public readonly record struct RiskAssessmentId(Guid Value)
{
    public static RiskAssessmentId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

/// <summary>Sujet évalué.</summary>
public enum RiskSubjectType
{
    Order = 0,
    User = 1,
    Seller = 2
}

/// <summary>Décision d'arbitrage du risque.</summary>
public enum RiskDecision
{
    Allow = 0,
    Review = 1,
    Block = 2
}

/// <summary>
/// Évaluation du risque d'une transaction ou d'un compte. Scoring par règles
/// (puis ML), traçant les signaux déclencheurs. Agrégat racine.
/// </summary>
public sealed class RiskAssessment : AggregateRoot<RiskAssessmentId>
{
    private RiskAssessment()
    {
    }

    private RiskAssessment(RiskAssessmentId id, RiskSubjectType subjectType, Guid subjectId, double score, string signals, RiskDecision decision)
        : base(id)
    {
        SubjectType = subjectType;
        SubjectId = subjectId;
        Score = score;
        Signals = signals;
        Decision = decision;
        EvaluatedAtUtc = DateTime.UtcNow;
    }

    public RiskSubjectType SubjectType { get; private set; }
    public Guid SubjectId { get; private set; }
    public double Score { get; private set; }
    public string Signals { get; private set; } = default!;
    public RiskDecision Decision { get; private set; }
    public DateTime EvaluatedAtUtc { get; private set; }

    public static Result<RiskAssessment> Create(RiskSubjectType subjectType, Guid subjectId, double score, string signals, RiskDecision decision)
    {
        if (subjectId == Guid.Empty)
        {
            return Error.Validation("fraud.subject_required", "Le sujet évalué est obligatoire.");
        }

        return new RiskAssessment(RiskAssessmentId.New(), subjectType, subjectId, Math.Clamp(score, 0d, 1d), signals ?? "{}", decision);
    }
}
