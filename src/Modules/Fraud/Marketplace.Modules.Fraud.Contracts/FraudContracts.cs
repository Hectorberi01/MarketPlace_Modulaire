namespace Marketplace.Modules.Fraud.Contracts;

/// <summary>Vue publique d'une évaluation de risque.</summary>
public sealed record RiskAssessmentSummary(
    Guid Id, string SubjectType, Guid SubjectId, double Score, string Signals, string Decision, DateTime EvaluatedAtUtc);
