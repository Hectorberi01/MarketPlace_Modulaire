using Marketplace.Modules.Fraud.Contracts;
using Marketplace.Modules.Fraud.Domain.Risk;

namespace Marketplace.Modules.Fraud.Infrastructure.Public;

/// <summary>Implémentation in-process de l'API publique du module Fraud.</summary>
internal sealed class FraudModuleApi : IFraudModuleApi
{
    private readonly IRiskAssessmentRepository _repository;

    public FraudModuleApi(IRiskAssessmentRepository repository) => _repository = repository;

    public async Task<string> GetDecisionAsync(string subjectType, Guid subjectId, CancellationToken cancellationToken = default)
    {
        // En l'absence d'évaluation, on autorise par défaut (fail-open au socle).
        if (!Enum.TryParse<RiskSubjectType>(subjectType, ignoreCase: true, out var type))
        {
            return RiskDecision.Allow.ToString();
        }

        var assessment = await _repository.GetLatestAsync(type, subjectId, cancellationToken);
        return (assessment?.Decision ?? RiskDecision.Allow).ToString();
    }
}
