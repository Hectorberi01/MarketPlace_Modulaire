namespace Marketplace.Modules.Fraud.Contracts;

/// <summary>
/// API in-process publique du module Fraud. Permet à Ordering/Payments de
/// consulter la dernière décision de risque d'un sujet (Allow / Review / Block).
/// </summary>
public interface IFraudModuleApi
{
    Task<string> GetDecisionAsync(string subjectType, Guid subjectId, CancellationToken cancellationToken = default);
}
