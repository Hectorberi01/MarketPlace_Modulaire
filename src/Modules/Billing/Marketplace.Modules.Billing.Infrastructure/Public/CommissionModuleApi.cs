using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.Modules.Billing.Contracts;
using Marketplace.Modules.Billing.Domain.Commissions;

namespace Marketplace.Modules.Billing.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Billing : résout la
/// règle de commission la plus spécifique et calcule la part plateforme.
/// À défaut de règle, applique le taux fixe par défaut (BillingOptions) — la
/// plateforme prélève ainsi TOUJOURS une commission sur chaque vente.
/// </summary>
internal sealed class CommissionModuleApi : ICommissionModuleApi
{
    private readonly ICommissionRuleRepository _repository;
    private readonly decimal _defaultRate;

    public CommissionModuleApi(ICommissionRuleRepository repository, BillingOptions options)
    {
        _repository = repository;
        _defaultRate = options.DefaultCommissionRate;
    }

    public async Task<CommissionResult> ComputeCommissionAsync(
        Guid sellerId, Guid categoryId, decimal grossAmount, string currency, CancellationToken cancellationToken = default)
    {
        var candidates = await _repository.GetCandidatesAsync(sellerId, categoryId, cancellationToken);
        var rule = CommissionResolver.Resolve(candidates, sellerId, categoryId, DateTime.UtcNow);

        // Règle spécifique si elle existe, sinon taux fixe par défaut (jamais 0 silencieux).
        var commission = rule?.ComputeCommission(grossAmount) ?? DefaultCommission(grossAmount, currency);
        return new CommissionResult(grossAmount, commission, grossAmount - commission, currency, rule?.Id.Value);
    }

    /// <summary>
    /// Commission au taux fixe par défaut, arrondie À LA PRÉCISION DE LA DEVISE et
    /// plafonnée au brut.
    ///
    /// C'était deux décimales en dur : en franc CFA, la commission par défaut de la
    /// plateforme sortait avec des centimes, alors que le grand livre et le versement
    /// vendeur travaillent à l'unité. Le mode <c>AwayFromZero</c> est conservé — il
    /// était explicite ici, contrairement au reste du dépôt, et l'enlever déplacerait
    /// les montants à mi-chemin sans rapport avec le défaut corrigé.
    /// </summary>
    private decimal DefaultCommission(decimal grossAmount, string currency)
    {
        if (_defaultRate <= 0m || grossAmount <= 0m)
        {
            return 0m;
        }

        var commission = decimal.Round(
            grossAmount * _defaultRate, Money.DecimalsFor(currency), MidpointRounding.AwayFromZero);
        return commission > grossAmount ? grossAmount : commission;
    }
}
