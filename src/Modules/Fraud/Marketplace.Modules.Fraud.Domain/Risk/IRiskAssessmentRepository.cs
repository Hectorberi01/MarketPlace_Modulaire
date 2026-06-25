namespace Marketplace.Modules.Fraud.Domain.Risk;

public interface IRiskAssessmentRepository
{
    Task AddAsync(RiskAssessment assessment, CancellationToken cancellationToken = default);

    /// <summary>Dernière évaluation d'un sujet (la décision en vigueur).</summary>
    Task<RiskAssessment?> GetLatestAsync(RiskSubjectType subjectType, Guid subjectId, CancellationToken cancellationToken = default);
}
