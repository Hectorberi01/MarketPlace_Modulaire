using Marketplace.Modules.Tax.Contracts;
using Marketplace.Modules.Tax.Domain.Taxes;

namespace Marketplace.Modules.Tax.Infrastructure.Public;

/// <summary>
/// Implémentation in-process de l'API publique du module Tax.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// LA DEVISE N'ARRIVE PAS JUSQU'ICI, ET C'EST UNE LIMITE ASSUMÉE.
///
/// <see cref="TaxRule"/> arrondit désormais selon la devise du montant. Mais le contrat
/// <see cref="ITaxModuleApi"/> ne transporte qu'une juridiction, une catégorie et un
/// montant : l'appelant (Ordering, au moment de placer la commande) connaît
/// <c>order.Currency</c> et ne le transmet pas. On applique donc la devise de la place.
///
/// C'est exact tant que la place ne vend qu'en franc CFA — ce qui est le cas — et faux
/// le jour où une commande sera libellée autrement : la TVA serait alors arrondie à
/// l'unité sur une devise à centimes. Le remède tient en deux gestes : ajouter la
/// devise à <see cref="ITaxModuleApi"/>, et la faire passer depuis le placement de
/// commande. Le second geste touche un fichier hors du périmètre de ce correctif.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
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
