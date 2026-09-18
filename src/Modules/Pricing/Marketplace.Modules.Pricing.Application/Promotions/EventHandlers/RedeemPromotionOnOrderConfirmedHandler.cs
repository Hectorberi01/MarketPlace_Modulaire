using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Pricing.Application.Abstractions;
using Marketplace.Modules.Pricing.Domain.Promotions;

namespace Marketplace.Modules.Pricing.Application.Promotions.EventHandlers;

/// <summary>
/// Décompte une promotion quand la commande qui l'utilisait est CONFIRMÉE.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// CE HANDLER N'EXISTAIT PAS, ET SANS LUI AUCUNE LIMITE DE COUPON N'ÉTAIT RÉELLE.
///
/// `Promotion.RecordUsage()` était écrit, testé, documenté — et n'avait AUCUN appelant
/// dans tout le dépôt. `UsedCount` restait donc à 0 pour l'éternité, et la condition
/// `UsedCount &lt; UsageLimit` était donc TOUJOURS vraie. Une promotion « 100 premiers
/// clients » servait le 100 001ᵉ. Le plafond était une décoration.
/// ═════════════════════════════════════════════════════════════════════════════
///
/// <para>
/// <b>Pourquoi à la CONFIRMATION et pas au checkout.</b> Une commande peut être placée
/// puis échouer au paiement. Décompter au checkout brûlerait un coupon pour une vente qui
/// n'a jamais eu lieu — il faudrait alors le rendre en compensation, donc une saga de plus,
/// donc une nouvelle source de bugs d'argent. Ici, seule une vente réellement encaissée
/// consomme le coupon. Le pire cas est qu'un coupon reste disponible quelques minutes de
/// trop pendant qu'un paiement est en vol : bénin, et réversible par nature.
/// </para>
///
/// <para>
/// <b>Idempotence.</b> L'outbox est <b>at-least-once</b> : ce même événement PEUT arriver
/// deux fois. Deux gardes, et il en faut deux :
/// </para>
/// <list type="number">
///   <item>la garde applicative <c>ExistsAsync</c> — confort, elle évite le coût d'une
///         violation de contrainte sur le chemin normal de rejeu ;</item>
///   <item>l'index unique <c>(PromotionId, OrderId)</c> — <b>la vraie garantie</b>. Entre
///         le SELECT de la garde et l'INSERT, deux livraisons concurrentes peuvent toutes
///         deux se croire les premières. Seule la base ferme cette fenêtre.</item>
/// </list>
///
/// <para>
/// <b>Concurrence.</b> <c>Redeem()</c> incrémente <c>UsedCount</c>, donc salit la ligne
/// <c>promotions</c>, qui porte un jeton <c>xmin</c>. Toutes les rédemptions d'une même
/// promotion se sérialisent donc sur cette ligne : deux commandes ne peuvent pas consommer
/// le dernier exemplaire d'un coupon en même temps. C'est aussi ce qui rend fiable le
/// comptage par acheteur ci-dessous, qui repose sur un COUNT lu juste avant l'écriture.
/// </para>
///
/// <para>
/// <b>Ce handler ne s'exécute PAS dans une requête HTTP</b> mais dans l'OutboxProcessor.
/// Un conflit de concurrence n'y devient donc pas un 409 : il fait échouer le message, qui
/// sera rejoué après temporisation (backoff exponentiel). Au bout de 10 tentatives, il part
/// en LETTRE MORTE — journalisée en Critical, comptée par `outbox_dead_letters_total`, et
/// listée dans /admin/outbox/dead-letters. Un coupon non décompté ne disparaît donc plus en
/// silence : il devient une alerte.
/// </para>
/// </summary>
public sealed class RedeemPromotionOnOrderConfirmedHandler : IIntegrationEventHandler<OrderConfirmedIntegrationEvent>
{
    private readonly IPromotionRepository _promotions;
    private readonly IPromotionRedemptionRepository _redemptions;
    private readonly IPricingUnitOfWork _unitOfWork;
    private readonly ILogger<RedeemPromotionOnOrderConfirmedHandler> _logger;

    public RedeemPromotionOnOrderConfirmedHandler(
        IPromotionRepository promotions,
        IPromotionRedemptionRepository redemptions,
        IPricingUnitOfWork unitOfWork,
        ILogger<RedeemPromotionOnOrderConfirmedHandler> logger)
    {
        _promotions = promotions;
        _redemptions = redemptions;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(OrderConfirmedIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        // L'immense majorité des commandes n'utilise aucun coupon : sortie immédiate,
        // sans toucher la base.
        if (string.IsNullOrWhiteSpace(integrationEvent.PromotionCode))
        {
            return;
        }

        var promotion = await _promotions.GetByCodeAsync(integrationEvent.PromotionCode, cancellationToken);
        if (promotion is null)
        {
            // La promotion a été supprimée entre le checkout et la confirmation. La vente
            // est faite, la remise a été accordée : on ne l'annule pas rétroactivement.
            // On journalise, car c'est une anomalie de gestion — pas une panne technique.
            _logger.LogWarning(
                "Commande {OrderId} confirmée avec le code {Code}, mais aucune promotion ne porte ce code. Rien à décompter.",
                integrationEvent.OrderId, integrationEvent.PromotionCode);
            return;
        }

        // Garde de confort : rejeu d'outbox déjà traité. La VRAIE garantie est l'index
        // unique (PromotionId, OrderId) — voir PromotionRedemptionConfiguration.
        if (await _redemptions.ExistsAsync(promotion.Id, integrationEvent.OrderId, cancellationToken))
        {
            _logger.LogDebug(
                "Promotion {PromotionId} déjà décomptée pour la commande {OrderId} — rejeu ignoré.",
                promotion.Id.Value, integrationEvent.OrderId);
            return;
        }

        var priorRedemptions = await _redemptions.CountForBuyerAsync(
            promotion.Id, integrationEvent.BuyerId, cancellationToken);

        var redeemResult = promotion.Redeem(priorRedemptions);
        if (redeemResult.IsFailure)
        {
            // Le plafond a été atteint entre le moment où l'acheteur a appliqué le code et
            // celui où sa commande a été confirmée (une autre commande est passée avant).
            //
            // ON NE REJETTE PAS L'ÉVÉNEMENT. La vente est encaissée, la remise a déjà été
            // accordée sur le montant payé. Lever ici ferait rejouer le message toutes les
            // 5 secondes, indéfiniment, pour une erreur qui ne se résoudra jamais — un
            // message empoisonné, exactement ce qui sature les logs.
            //
            // On absorbe donc, et on journalise en Warning : la perte est bornée (une
            // remise de trop), et elle est visible.
            _logger.LogWarning(
                "Promotion {Code} non décomptée pour la commande {OrderId} : {Error}. La remise a néanmoins été accordée — plafond dépassé de justesse.",
                integrationEvent.PromotionCode, integrationEvent.OrderId, redeemResult.Error.Message);
            return;
        }

        await _redemptions.AddAsync(
            PromotionRedemption.For(promotion.Id, integrationEvent.BuyerId, integrationEvent.OrderId, DateTime.UtcNow),
            cancellationToken);

        // Une seule transaction : l'incrément d'UsedCount et la ligne de rédemption
        // partent ensemble, ou pas du tout.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Promotion {Code} décomptée pour la commande {OrderId} ({Used}/{Limit}).",
            integrationEvent.PromotionCode, integrationEvent.OrderId,
            promotion.UsedCount, promotion.UsageLimit == 0 ? "∞" : promotion.UsageLimit.ToString());
    }
}
