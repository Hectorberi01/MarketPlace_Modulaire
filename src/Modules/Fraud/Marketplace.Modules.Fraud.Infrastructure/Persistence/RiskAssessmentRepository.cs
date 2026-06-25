using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Fraud.Domain.Risk;

namespace Marketplace.Modules.Fraud.Infrastructure.Persistence;

internal sealed class RiskAssessmentRepository : IRiskAssessmentRepository
{
    private readonly FraudDbContext _dbContext;

    public RiskAssessmentRepository(FraudDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(RiskAssessment assessment, CancellationToken cancellationToken = default)
        => await _dbContext.Assessments.AddAsync(assessment, cancellationToken);

    public async Task<RiskAssessment?> GetLatestAsync(RiskSubjectType subjectType, Guid subjectId, CancellationToken cancellationToken = default)
        => await _dbContext.Assessments
            .Where(a => a.SubjectType == subjectType && a.SubjectId == subjectId)
            .OrderByDescending(a => a.EvaluatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
}
