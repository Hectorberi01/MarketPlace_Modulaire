using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Analytics.Domain.Metrics;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Payments.Contracts.IntegrationEvents;
using Marketplace.Modules.Returns.Contracts.IntegrationEvents;
using Marketplace.Modules.Settlement.Contracts.IntegrationEvents;

namespace Marketplace.Modules.Analytics.Application.Metrics;

/// <summary>Une part de remboursement imputée à un périmètre donné.</summary>
public readonly record struct PartRemboursee(MetricScope Scope, Guid? OwnerId, decimal Montant);

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// IMPUTE UN REMBOURSEMENT AU SEAU DE LA COMMANDE.
///
/// Les trois flux de remboursement de la place — retour produit, remboursement de
/// paiement, geste commercial de l'administrateur — n'ont en commun que leur effet :
/// de l'argent revient au client, et le chiffre d'affaires d'une période passée cesse
/// d'être vrai. Ce qui les distingue (ce qu'ils portent, à qui c'est imputable) reste
/// dans leur gestionnaire respectif ; ce qui les réunit est ici.
///
/// LA PÉRIODE VISÉE EST CELLE DE LA COMMANDE, PAS CELLE DU REMBOURSEMENT.
///
/// Un retour traité en mai sur une commande de mars corrige mars. C'est ce qui permet
/// de comparer deux mois entre eux, et c'est cohérent avec l'accumulation, qui date
/// déjà par `order.CreatedAtUtc`. Le prix est qu'un chiffre déjà lu peut changer —
/// d'où `UpdatedAtUtc`, que l'écran affiche.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class ImputationRemboursement
{
    private readonly IMetricSnapshotRepository _repository;
    private readonly IOrderingModuleApi _ordering;
    private readonly IAnalyticsUnitOfWork _unitOfWork;
    private readonly ILogger<ImputationRemboursement> _logger;

    public ImputationRemboursement(
        IMetricSnapshotRepository repository,
        IOrderingModuleApi ordering,
        IAnalyticsUnitOfWork unitOfWork,
        ILogger<ImputationRemboursement> logger)
    {
        _repository = repository;
        _ordering = ordering;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task AppliquerAsync(
        string source,
        Guid sourceId,
        Guid orderId,
        string devise,
        Func<OrderSummary, IEnumerable<PartRemboursee>> repartir,
        CancellationToken cancellationToken)
    {
        if (await _repository.IsRefundProcessedAsync(source, sourceId, cancellationToken))
        {
            return;
        }

        var order = await _ordering.GetOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            // ON LÈVE, ET ON NE MARQUE PAS. Rien n'efface une commande dans ce dépôt :
            // ne pas la trouver veut dire que le module Ordering n'a pas répondu. Poser
            // le marqueur rendrait la perte DÉFINITIVE et silencieuse — aucun écran ne
            // liste les marqueurs, aucun endpoint ne les purge. En levant, le message
            // repasse par les réessais de l'outbox, et s'il n'aboutit pas il finit en
            // lettre morte : visible sur l'écran Outbox, et rejouable.
            throw new InvalidOperationException(
                $"Analytics — commande {orderId} introuvable pour le remboursement {source}:{sourceId}.");
        }

        if (!string.Equals(order.Currency, devise, StringComparison.OrdinalIgnoreCase))
        {
            // Ici en revanche c'est une incohérence de DONNÉES, que le temps ne
            // réparera pas : additionner deux devises dans un même seau ferait un
            // nombre qui ne veut rien dire. On classe sans suite, en le journalisant.
            _logger.LogWarning(
                "Analytics — remboursement {Source}:{SourceId} en {DeviseRemboursement} sur une commande en {DeviseCommande}. Ignoré.",
                source, sourceId, devise, order.Currency);
            await MarquerAsync(source, sourceId, orderId, 0m, cancellationToken);
            return;
        }

        var parts = repartir(order).Where(p => p.Montant > 0m).ToList();

        // ═══════════════════════════════════════════════════════════════════════════
        // ON NE PEUT PAS RETRANCHER PLUS QU'ON N'A COMPTÉ.
        //
        // Les trois voies de remboursement ignorent l'existence des deux autres, et
        // deux d'entre elles peuvent viser la même commande : un retour partiel rendu
        // au client, puis un remboursement du paiement entier décidé au back-office.
        // Chacune retranchait son montant sans voir l'autre, et le chiffre d'affaires
        // d'une période finissait par baisser de plus que la commande n'avait rapporté.
        //
        // Le net négatif était pensé comme le signal d'une ACCUMULATION MANQUANTE ; le
        // laisser signifier aussi « déduction en trop » aurait rendu les deux cas
        // indiscernables. Le plafond est donc posé ici, sur la seule grandeur qui les
        // couvre toutes : ce que la commande a apporté au périmètre plateforme.
        //
        // Quand le plafond mord, TOUTES les parts sont réduites du même rapport —
        // vendeurs compris. C'est une approximation, et elle est assumée : à ce stade
        // on sait qu'une des trois voies a remboursé plus que de raison, et mieux vaut
        // une répartition proportionnelle qu'un vendeur ramené arbitrairement à zéro.
        // ═══════════════════════════════════════════════════════════════════════════
        var dejaRetranche = await _repository.SumRefundedForOrderAsync(orderId, cancellationToken);
        var restant = order.GrandTotal - dejaRetranche;
        var partPlateforme = parts
            .Where(p => p.Scope == MetricScope.Platform)
            .Sum(p => p.Montant);

        var rapport = 1m;
        if (restant <= 0m)
        {
            _logger.LogWarning(
                "Analytics — commande {OrderId} déjà remboursée à hauteur de {Deja} sur {Total} : {Source}:{SourceId} n'est pas imputé.",
                orderId, dejaRetranche, order.GrandTotal, source, sourceId);
            await MarquerAsync(source, sourceId, orderId, 0m, cancellationToken);
            return;
        }

        if (partPlateforme > restant)
        {
            rapport = restant / partPlateforme;
            _logger.LogWarning(
                "Analytics — remboursement {Source}:{SourceId} plafonné : {Demande} demandés, {Restant} disponibles sur la commande {OrderId}.",
                source, sourceId, partPlateforme, restant, orderId);
        }

        var dayKey = order.CreatedAtUtc.ToString("yyyy-MM-dd");
        var monthKey = order.CreatedAtUtc.ToString("yyyy-MM");

        foreach (var part in parts)
        {
            var montant = rapport == 1m ? part.Montant : Math.Round(part.Montant * rapport, 2, MidpointRounding.AwayFromZero);
            if (montant <= 0m)
            {
                continue;
            }

            var ajuste = part with { Montant = montant };
            await RembourserAsync(ajuste, dayKey, MetricGranularity.Day, order.Currency, cancellationToken);
            await RembourserAsync(ajuste, monthKey, MetricGranularity.Month, order.Currency, cancellationToken);
        }

        // Le montant CONSOMMÉ du plafond est celui du périmètre plateforme : c'est lui
        // qui se compare au total de la commande. Les parts vendeur suivent une autre
        // assiette (la somme de leurs lignes) et ne s'y additionnent pas.
        var consomme = rapport == 1m ? partPlateforme : Math.Min(partPlateforme, restant);
        await MarquerAsync(source, sourceId, orderId, consomme, cancellationToken);
    }

    private async Task RembourserAsync(
        PartRemboursee part, string periodKey, MetricGranularity granularity, string currency, CancellationToken ct)
    {
        var snapshot = await _repository.GetAsync(part.Scope, part.OwnerId, periodKey, granularity, ct);

        if (snapshot is null)
        {
            // Un remboursement dont la vente n'a jamais été comptée : événement perdu,
            // seau purgé, ou module branché après coup. On CRÉE le seau plutôt que de
            // se taire — il ressortira en net négatif, et c'est précisément le signal
            // qu'il manque une accumulation. Se taire aurait laissé croire que tout
            // concorde.
            _logger.LogWarning(
                "Analytics — remboursement imputé à un seau absent ({Scope}/{Owner}/{Period}/{Granularity}). Le net sera négatif.",
                part.Scope, part.OwnerId, periodKey, granularity);
            snapshot = MetricSnapshot.Create(part.Scope, part.OwnerId, periodKey, granularity, currency);
            await _repository.AddAsync(snapshot, ct);
        }
        else if (!string.Equals(snapshot.Currency, currency, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Analytics — seau {Scope}/{Owner}/{Period} en {DeviseSeau}, remboursement en {DeviseRemboursement}. Non imputé.",
                part.Scope, part.OwnerId, periodKey, snapshot.Currency, currency);
            return;
        }

        snapshot.Rembourser(part.Montant);
    }

    private async Task MarquerAsync(string source, Guid sourceId, Guid orderId, decimal montant, CancellationToken ct)
    {
        await _repository.MarkRefundProcessedAsync(ProcessedRefund.Mark(source, sourceId, orderId, montant), ct);
        // UN SEUL SaveChanges : le marqueur et les mutations d'instantané partent
        // ensemble. Ni « retranché sans marqueur » (rejoué, donc compté deux fois), ni
        // « marqué sans retrancher » (perdu à jamais) ne sont possibles.
        await _unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Retour produit remboursé : l'argent est arrivé au client.
///
/// Le montant est plafonné par le domaine au `LineTotal` de la ligne retournée, et
/// l'événement porte le vendeur : l'imputation vendeur est donc exacte, sans avoir à
/// répartir quoi que ce soit.
/// </summary>
public sealed class DeduireMetriquesSurRetourRembourseHandler : IIntegrationEventHandler<ReturnRefundedIntegrationEvent>
{
    private readonly ImputationRemboursement _imputation;

    public DeduireMetriquesSurRetourRembourseHandler(ImputationRemboursement imputation) => _imputation = imputation;

    public Task HandleAsync(ReturnRefundedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => _imputation.AppliquerAsync(
            ProcessedRefund.SourceRetour,
            integrationEvent.ReturnRequestId,
            integrationEvent.OrderId,
            integrationEvent.Currency,
            _ => new[]
            {
                new PartRemboursee(MetricScope.Platform, null, integrationEvent.RefundAmount),
                new PartRemboursee(MetricScope.Seller, integrationEvent.SellerId, integrationEvent.RefundAmount),
            },
            cancellationToken);
}

/// <summary>
/// Paiement remboursé au prestataire : la commande entière est défaite.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// LES DEUX PÉRIMÈTRES NE REÇOIVENT PAS LE MÊME MONTANT, ET C'EST DÉJÀ VRAI À LA VENTE.
///
/// L'accumulation impute au périmètre PLATEFORME le `GrandTotal` — livraison comprise,
/// part payée par la cagnotte comprise — et à chaque VENDEUR la somme des `LineTotal`
/// de ses lignes. Les deux ne s'égalisent donc jamais.
///
/// La déduction suit la même règle, sans quoi elle laisserait un résidu : la plateforme
/// se voit retirer le montant réellement remboursé (`Payment.Amount`, c'est-à-dire
/// `GrandTotal − cagnotte appliquée`), et chaque vendeur ses propres lignes.
///
/// Ce qui reste au périmètre plateforme après un remboursement total, c'est exactement
/// la part que la cagnotte avait couverte — de l'argent que le client n'avait pas
/// versé, et que ce remboursement ne lui a donc pas rendu.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DeduireMetriquesSurPaiementRembourseHandler : IIntegrationEventHandler<PaymentRefundedIntegrationEvent>
{
    private readonly ImputationRemboursement _imputation;

    public DeduireMetriquesSurPaiementRembourseHandler(ImputationRemboursement imputation) => _imputation = imputation;

    public Task HandleAsync(PaymentRefundedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => _imputation.AppliquerAsync(
            ProcessedRefund.SourcePaiement,
            integrationEvent.PaymentId,
            integrationEvent.OrderId,
            integrationEvent.Currency,
            order =>
            {
                var parts = new List<PartRemboursee>
                {
                    new(MetricScope.Platform, null, integrationEvent.Amount),
                };

                parts.AddRange(order.Lines
                    .GroupBy(l => l.SellerId)
                    .Select(g => new PartRemboursee(MetricScope.Seller, g.Key, g.Sum(l => l.LineTotal))));

                return parts;
            },
            cancellationToken);
}

/// <summary>
/// Remboursement direct décidé par l'administrateur : geste commercial.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// IMPUTÉ À LA PLATEFORME SEULE, ET C'EST UN CHOIX.
///
/// Deux raisons, dans cet ordre :
///
///   - Le montant est LIBRE. Sur une commande à plusieurs vendeurs, rien ne dit lequel
///     est concerné : répartir au prorata des lignes inventerait une attribution que
///     personne n'a décidée, et ferait baisser le chiffre d'affaires d'un vendeur qui
///     n'y est pour rien.
///   - Un geste commercial de la plateforme est un COÛT DE LA PLATEFORME. Il l'est
///     déjà côté portefeuille, où il est débité du compte plateforme et non des
///     vendeurs. Le traiter autrement ici ferait diverger les deux lectures.
///
/// Conséquence assumée : le rapport d'un vendeur ne bouge pas quand la plateforme
/// rembourse un de ses clients de son propre chef. C'est cohérent — le vendeur a bien
/// vendu, et il sera payé.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class DeduireMetriquesSurGesteCommercialHandler : IIntegrationEventHandler<CustomerRefundSettledIntegrationEvent>
{
    private readonly ImputationRemboursement _imputation;

    public DeduireMetriquesSurGesteCommercialHandler(ImputationRemboursement imputation) => _imputation = imputation;

    public Task HandleAsync(CustomerRefundSettledIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        => _imputation.AppliquerAsync(
            ProcessedRefund.SourceGesteCommercial,
            integrationEvent.CustomerRefundId,
            integrationEvent.OrderId,
            integrationEvent.Currency,
            _ => new[] { new PartRemboursee(MetricScope.Platform, null, integrationEvent.Amount) },
            cancellationToken);
}
