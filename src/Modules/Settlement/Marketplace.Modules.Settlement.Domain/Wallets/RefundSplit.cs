namespace Marketplace.Modules.Settlement.Domain.Wallets;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// PAR QUEL CANAL RENDRE COMBIEN.
///
/// Fonction PURE, sans dépendance ni accès base — c'est ce qui la rend testable, et
/// c'est là que vit la seule règle qui compte : ce qui est entré en espèces ressort en
/// espèces, le reste retourne en cagnotte.
///
/// POURQUOI CETTE RÈGLE EXISTE
///
/// Une commande de 13 000 réglée 8 000 au prestataire et 5 000 sur la cagnotte n'a fait
/// entrer que 8 000 en caisse. Rembourser 13 000 en Mobile Money n'aurait été ni une
/// perte ni un déséquilibre comptable — la cagnotte dépensée était déjà une dette de la
/// plateforme. C'eût été une CONVERSION DE DETTE EN SORTIE DE TRÉSORERIE, décidée par un
/// administrateur, majorée des frais de versement, sur une somme que le client aurait pu
/// ne jamais retirer.
///
/// L'EXCÉDENT N'EST PAS REFUSÉ, IL EST REDIRIGÉ
///
/// Refuser aurait été plus simple et faux : aucune route ne crédite une cagnotte hors du
/// flux « retour », et une commande entièrement réglée en cagnotte serait alors devenue
/// impossible à rembourser. La somme des deux parts vaut TOUJOURS le montant demandé.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public readonly record struct RefundSplit(decimal CashAmount, decimal WalletAmount)
{
    /// <summary>Le montant total réparti. Égal, par construction, au montant demandé.</summary>
    public decimal Total => CashAmount + WalletAmount;

    /// <summary>Un versement au prestataire est-il nécessaire ?</summary>
    public bool RequiresPayout => CashAmount > 0m;

    /// <summary>
    /// Répartit un remboursement entre espèces et cagnotte.
    /// </summary>
    /// <param name="amount">Montant demandé. Négatif ou nul donne une répartition vide.</param>
    /// <param name="cashCollected">
    /// Ce que la commande a fait entrer en caisse — <c>GrandTotal moins WalletApplied</c>.
    /// </param>
    /// <param name="cashAlreadyRefunded">
    /// Part espèces déjà rendue sur cette commande. Les remboursements issus du flux
    /// « retour » n'en font PAS partie : ils créditent la cagnotte, jamais la caisse.
    /// </param>
    public static RefundSplit Compute(decimal amount, decimal cashCollected, decimal cashAlreadyRefunded)
    {
        if (amount <= 0m)
        {
            return new RefundSplit(0m, 0m);
        }

        // Le disponible en espèces ne peut pas être négatif. Il le deviendrait si des
        // remboursements directs dépassaient l'encaissement — situation que le plafond
        // de valeur interdit, mais qu'une donnée ancienne ou abîmée peut porter. On
        // borne plutôt que de produire une part espèces négative, qui remonterait en
        // cagnotte SUPÉRIEURE au montant demandé.
        var remainingCash = Math.Max(0m, cashCollected - cashAlreadyRefunded);

        var cash = Math.Min(amount, remainingCash);

        // La part cagnotte est un RESTE, jamais un second calcul : c'est ce qui garantit
        // que les deux parts redonnent exactement le montant demandé, sans centime
        // évaporé à l'arrondi.
        return new RefundSplit(cash, amount - cash);
    }
}
