namespace Marketplace.Modules.Analytics.Domain.Metrics;

/// <summary>Périmètre d'agrégation des métriques.</summary>
public enum MetricScope
{
    Platform = 0,
    Seller = 1
}

/// <summary>Granularité temporelle d'un instantané.</summary>
public enum MetricGranularity
{
    Day = 0,
    Month = 1
}

/// <summary>
/// Instantané agrégé de métriques (GMV, nombre de commandes, articles vendus)
/// pour un périmètre (plateforme ou vendeur), une période et une granularité.
/// Read model accumulé hors du chemin transactionnel via les événements de
/// commande ; jamais source de vérité financière (cf. Settlement / Billing).
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// LE CHIFFRE D'AFFAIRES NE DIMINUAIT JAMAIS.
///
/// Cet instantané n'avait qu'une mutation, <see cref="Accrue"/>, appelée à la
/// confirmation d'une commande. Aucun remboursement, aucun retour ne le touchait :
/// un article retourné et remboursé restait dans le chiffre d'affaires pour toujours.
///
/// LES REMBOURSEMENTS SONT UNE COLONNE À PART, PAS UNE SOUSTRACTION.
///
/// Retrancher directement de <see cref="GmvAmount"/> aurait été plus court, et aurait
/// détruit de l'information : on n'aurait plus su distinguer « un mois à 900 000 » de
/// « un mois à 1 000 000 dont 100 000 rendus », alors que ce sont deux situations
/// commerciales opposées. Le brut est donc conservé intact, les remboursements
/// s'accumulent à côté, et le net se calcule — <see cref="NetGmv"/>.
///
/// Corollaire pratique : la correction est ADDITIVE. Rien de ce qui a été accumulé
/// n'est réécrit, ce qui est la bonne propriété pour un read model reconstruit par
/// rejeu d'événements.
///
/// LA DÉDUCTION VISE LE SEAU DE LA COMMANDE, PAS CELUI DU REMBOURSEMENT.
///
/// Un retour traité en mai sur une commande de mars corrige MARS. « Chiffre d'affaires
/// de mars » veut donc dire « valeur nette de ce qui a été vendu en mars », et non
/// « argent encaissé en mars ». C'est la lecture commerciale, celle qui permet de
/// comparer deux mois ; le prix à payer est qu'un chiffre déjà lu peut changer.
/// <see cref="UpdatedAtUtc"/> est ce qui permet de s'en apercevoir.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class MetricSnapshot
{
    private MetricSnapshot()
    {
    }

    private MetricSnapshot(Guid id, MetricScope scope, Guid? ownerId, string periodKey, MetricGranularity granularity, string currency)
    {
        Id = id;
        Scope = scope;
        OwnerId = ownerId;
        PeriodKey = periodKey;
        Granularity = granularity;
        Currency = currency;
        OrdersCount = 0;
        ItemsSold = 0;
        GmvAmount = 0m;
        RefundedAmount = 0m;
        RefundsCount = 0;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public MetricScope Scope { get; private set; }
    public Guid? OwnerId { get; private set; }
    public string PeriodKey { get; private set; } = default!;
    public MetricGranularity Granularity { get; private set; }
    public string Currency { get; private set; } = default!;
    public int OrdersCount { get; private set; }
    public int ItemsSold { get; private set; }

    /// <summary>Valeur BRUTE confirmée sur la période. Ne diminue jamais.</summary>
    public decimal GmvAmount { get; private set; }

    /// <summary>Total rendu aux clients au titre des commandes de cette période.</summary>
    public decimal RefundedAmount { get; private set; }

    /// <summary>Nombre de remboursements appliqués. Sert à lire un montant en contexte.</summary>
    public int RefundsCount { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Valeur nette : ce qui a été vendu, moins ce qui a été rendu.
    ///
    /// PEUT ÊTRE NÉGATIF, et c'est voulu : un remboursement dont la vente n'a jamais
    /// été comptée — événement perdu, seau reconstruit — produit un net négatif, qui
    /// est exactement le signal qu'il faut voir. Le masquer par un plancher à zéro
    /// transformerait une incohérence visible en chiffre plausible.
    /// </summary>
    public decimal NetGmv => GmvAmount - RefundedAmount;

    public static MetricSnapshot Create(MetricScope scope, Guid? ownerId, string periodKey, MetricGranularity granularity, string currency)
        => new(Guid.NewGuid(), scope, ownerId, periodKey, granularity, currency);

    /// <summary>Incrémente l'instantané d'une commande (GMV + articles + 1 commande).</summary>
    public void Accrue(decimal gmvAmount, int itemsCount)
    {
        OrdersCount += 1;
        ItemsSold += itemsCount;
        GmvAmount += gmvAmount;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Enregistre un remboursement imputé à cette période.
    ///
    /// <see cref="OrdersCount"/> et <see cref="ItemsSold"/> NE BOUGENT PAS. Une commande
    /// confirmée a bien eu lieu, même remboursée ensuite : la décompter réécrirait
    /// l'histoire, et aucun des trois flux de remboursement ne dit combien d'unités
    /// sont concernées — un retour porte un montant et une offre, jamais une quantité.
    /// Un compteur d'articles qu'on ne saurait décrémenter correctement vaut mieux
    /// juste que faux.
    /// </summary>
    public void Rembourser(decimal montant)
    {
        if (montant <= 0m)
        {
            return;
        }

        RefundedAmount += montant;
        RefundsCount += 1;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
