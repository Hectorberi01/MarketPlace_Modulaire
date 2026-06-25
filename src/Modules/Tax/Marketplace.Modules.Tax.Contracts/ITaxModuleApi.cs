namespace Marketplace.Modules.Tax.Contracts;

/// <summary>
/// API in-process publique du module Tax. Permet à Ordering/Cart de calculer la
/// taxe applicable sur un montant, par juridiction et catégorie.
/// </summary>
public interface ITaxModuleApi
{
    Task<TaxResult> ComputeTaxAsync(string jurisdiction, Guid categoryId, decimal amount, CancellationToken cancellationToken = default);
}
