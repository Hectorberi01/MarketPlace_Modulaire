using Marketplace.Modules.Tax.Contracts;
using Marketplace.Modules.Tax.Domain.Taxes;

namespace Marketplace.Modules.Tax.Infrastructure.Public;

/// <summary>Implémentation in-process de l'API publique du module Tax.</summary>
internal sealed class TaxModuleApi : ITaxModuleApi
{
    private readonly ITaxRuleRepository _repository;

    public TaxModuleApi(ITaxRuleRepository repository) => _repository = repository;

    public async Task<TaxResult> ComputeTaxAsync(string jurisdiction, Guid categoryId, decimal amount, CancellationToken cancellationToken = default)
    {
        var candidates = await _repository.GetCandidatesAsync(jurisdiction, categoryId, cancellationToken);
        var rule = TaxResolver.Resolve(candidates, jurisdiction, categoryId, DateTime.UtcNow);

        var tax = rule?.ComputeTax(amount) ?? 0m;
        return new TaxResult(amount, tax, amount + tax, rule?.Type.ToString() ?? "None", rule?.Id.Value);
    }
}
