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
        var rule = await ResolveAsync(jurisdiction, categoryId, cancellationToken);

        var tax = rule?.ComputeTax(amount) ?? 0m;
        return new TaxResult(amount, tax, amount + tax, rule?.Type.ToString() ?? "None", rule?.Id.Value);
    }

    public async Task<IncludedTaxResult> ExtractIncludedTaxAsync(
        string jurisdiction, Guid categoryId, decimal grossAmount, CancellationToken cancellationToken = default)
    {
        var rule = await ResolveAsync(jurisdiction, categoryId, cancellationToken);

        var tax = rule?.ExtractIncludedTax(grossAmount) ?? 0m;

        // LE HORS TAXE EST UN RESTE, PAS UN CALCUL. Le déduire par soustraction est ce
        // qui garantit l'égalité HT + TVA = TTC au centime près. Une seconde division
        // (grossAmount / (1 + taux)) donnerait un couple qui, additionné, peut manquer
        // le montant réellement encaissé — et une comptabilité qui ne retombe pas sur
        // le montant encaissé n'est pas une comptabilité.
        return new IncludedTaxResult(
            grossAmount,
            grossAmount - tax,
            tax,
            rule?.Rate ?? 0m,
            rule?.Type.ToString() ?? "None",
            rule?.Id.Value);
    }

    private async Task<TaxRule?> ResolveAsync(string jurisdiction, Guid categoryId, CancellationToken cancellationToken)
    {
        var candidates = await _repository.GetCandidatesAsync(jurisdiction, categoryId, cancellationToken);
        return TaxResolver.Resolve(candidates, jurisdiction, categoryId, DateTime.UtcNow);
    }
}
