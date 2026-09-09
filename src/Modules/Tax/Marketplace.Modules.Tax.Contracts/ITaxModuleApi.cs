namespace Marketplace.Modules.Tax.Contracts;

/// <summary>
/// API in-process publique du module Tax.
///
/// DEUX SENS DE CALCUL, ET ILS NE SONT PAS INTERCHANGEABLES.
///
/// <see cref="ComputeTaxAsync"/> AJOUTE la taxe à un montant hors taxe : c'est le
/// régime « prix HT + TVA ». <see cref="ExtractIncludedTaxAsync"/> EXTRAIT la taxe
/// d'un montant qui la contient déjà : c'est le régime « TVA comprise », celui
/// retenu par la place — le prix saisi par le vendeur est le prix payé par
/// l'acheteur, et le total de la commande ne bouge pas.
///
/// Les deux coexistent parce que le second est le besoin réel du parcours d'achat
/// et le premier reste la primitive juste pour une simulation ou un devis hors
/// taxe. Confondre les deux fausse la déclaration de TVA sans qu'aucun total
/// affiché ne change — le pire genre d'erreur, celle qui ne se voit pas.
/// </summary>
public interface ITaxModuleApi
{
    /// <summary>
    /// Taxe à AJOUTER à un montant hors taxe. Ne convient PAS au calcul de la taxe
    /// contenue dans un prix de vente : voir <see cref="ExtractIncludedTaxAsync"/>.
    /// </summary>
    Task<TaxResult> ComputeTaxAsync(string jurisdiction, Guid categoryId, decimal amount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ventilation hors taxe / taxe d'un montant TOUTES TAXES COMPRISES.
    ///
    /// Renvoie systématiquement un résultat : l'absence de règle applicable n'est pas
    /// une erreur, c'est une taxe nulle. Une place qui démarre sans règle fiscale
    /// enregistrée doit continuer à prendre des commandes.
    /// </summary>
    Task<IncludedTaxResult> ExtractIncludedTaxAsync(string jurisdiction, Guid categoryId, decimal grossAmount, CancellationToken cancellationToken = default);
}
