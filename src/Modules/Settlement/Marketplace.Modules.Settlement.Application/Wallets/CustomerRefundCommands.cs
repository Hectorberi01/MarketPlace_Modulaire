using FluentValidation;
using Microsoft.Extensions.Logging;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Payments.Contracts;
using Marketplace.Modules.Returns.Contracts;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Contracts;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Application.Wallets;

// ============================================================================
// Remboursement DIRECT d'un client, initié par l'admin sur une commande (hors flux
// « retour »). FedaPay ne remboursant pas une transaction via l'API, l'argent part
// par un PAYOUT Mobile Money vers le numéro du client, et le coût est débité du
// portefeuille plateforme (solde « refunds »). Statuts et gestion des issues
// indéterminées calqués sur les retraits vendeur : on ne rembourse jamais à
// l'aveugle un versement dont on ignore l'issue (risque de double versement).
// ============================================================================

/// <summary>
/// Rembourse un client sur une commande, EN RENDANT CHAQUE PART PAR SON CANAL D'ORIGINE.
///
/// Ce qui a été encaissé en espèces repart en espèces (payout FedaPay MoMo, coût débité
/// du portefeuille plateforme) ; ce qui avait été réglé avec la cagnotte y retourne. Le
/// montant total reste plafonné à la valeur de la commande, tous rails de remboursement
/// confondus — retours compris.
/// </summary>
public sealed record InitiateCustomerRefundCommand(
    Guid OrderId, decimal Amount, string Msisdn, string Provider, string Reason) : ICommand<CustomerRefundSplitView>;

public sealed class InitiateCustomerRefundCommandValidator : AbstractValidator<InitiateCustomerRefundCommand>
{
    public InitiateCustomerRefundCommandValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0m).WithMessage("Le montant doit être positif.");
        // NI `Msisdn` NI `Provider` NE SONT EXIGÉS ICI, et c'est délibéré.
        //
        // Ils ne servent qu'à la part remboursée en ESPÈCES. Une commande entièrement
        // réglée avec la cagnotte n'en a aucun usage, et les exiger rendrait ce
        // remboursement-là impossible sans un numéro fictif. Seul le handler sait s'il
        // y a une jambe espèces ; c'est donc lui qui exige le nécessaire, au moment où
        // il en a besoin.
        RuleFor(c => c.Msisdn).NotNull();
        RuleFor(c => c.Provider).NotNull();
        RuleFor(c => c.Reason).NotEmpty().WithMessage("Un motif est requis.");
    }
}

internal sealed class InitiateCustomerRefundCommandHandler : ICommandHandler<InitiateCustomerRefundCommand, CustomerRefundSplitView>
{
    private readonly ICustomerRefundRepository _refunds;
    private readonly WalletMutations _wallets;
    private readonly IOrderingModuleApi _orders;
    private readonly IReturnsModuleApi _returns;
    private readonly IPayoutModuleApi _payouts;
    private readonly ISettlementUnitOfWork _unitOfWork;

    public InitiateCustomerRefundCommandHandler(
        ICustomerRefundRepository refunds,
        WalletMutations wallets,
        IOrderingModuleApi orders,
        IReturnsModuleApi returns,
        IPayoutModuleApi payouts,
        ISettlementUnitOfWork unitOfWork)
    {
        _refunds = refunds;
        _wallets = wallets;
        _orders = orders;
        _returns = returns;
        _payouts = payouts;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CustomerRefundSplitView>> Handle(InitiateCustomerRefundCommand command, CancellationToken cancellationToken)
    {
        var order = await _orders.GetOrderAsync(command.OrderId, cancellationToken);
        if (order is null)
        {
            return Result.Failure<CustomerRefundSplitView>(Error.NotFound("settlement.order_not_found", "Commande introuvable."));
        }

        // On ne rembourse qu'une commande RÉELLEMENT ENCAISSÉE (Confirmed / Delivered).
        if (order.Status is not ("Confirmed" or "Delivered"))
        {
            return Result.Failure<CustomerRefundSplitView>(Error.Validation(
                "settlement.order_not_refundable", "Seule une commande encaissée (confirmée ou livrée) peut être remboursée ici."));
        }

        // ═════════════════════════════════════════════════════════════════════════
        // PLAFOND : LE TOTAL DE LA COMMANDE, LIVRAISON COMPRISE.
        //
        // C'était « GrandTotal + ShippingFee » — et les frais de livraison étaient donc
        // comptés DEUX FOIS, puisque GrandTotal les inclut déjà (Order.RecomputeTotals).
        // Le plafond dépassait le montant réellement encaissé, exactement du montant de
        // la livraison : sur une commande à 10 000 livrée à 1 500, l'admin pouvait
        // rembourser 13 000. Personne ne s'en apercevait tant que les remboursements
        // restaient inférieurs au total.
        //
        // GrandTotal reste la bonne borne même depuis la cagnotte : ce que le client a
        // réglé, c'est AmountDue au prestataire PLUS WalletApplied sur son portefeuille,
        // et leur somme fait GrandTotal.
        //
        // LES REMBOURSEMENTS ISSUS DU FLUX « RETOUR » SONT DÉSORMAIS NETTÉS.
        //
        // Ils ne l'étaient pas, et le commentaire qui occupait ces lignes le disait :
        // « À suivre si les deux flux doivent partager un plafond commun ». Ils le
        // devaient. Chacun bornait correctement sur SES propres écritures — celui-ci sur
        // la table des remboursements directs, le retour sur le montant de la ligne — et
        // aucun ne regardait l'autre. Sur une même commande, la plateforme pouvait donc
        // verser le total en espèces ET le total en cagnotte : environ deux fois la
        // valeur de la commande, avec deux contrôles qui passaient proprement.
        //
        // La somme vient de `IReturnsModuleApi` plutôt que d'une lecture directe : les
        // deux modules gardent leurs données, et se posent la question à voix haute.
        // ═════════════════════════════════════════════════════════════════════════
        var paidTotal = order.GrandTotal;
        var byDirect = await _refunds.SumActiveForOrderAsync(command.OrderId, cancellationToken);
        var byReturns = await _returns.SumCommittedRefundsForOrderAsync(command.OrderId, cancellationToken);
        var remaining = paidTotal - byDirect - byReturns;
        if (command.Amount > remaining)
        {
            return Result.Failure<CustomerRefundSplitView>(Error.Validation(
                "settlement.refund_exceeds_paid",
                $"Le remboursement dépasse le montant remboursable ({remaining} {order.Currency}), "
                + "remboursements sur retour compris."));
        }

        // ═════════════════════════════════════════════════════════════════════════
        // CHAQUE PART REPART PAR OÙ ELLE EST VENUE.
        //
        // Le plafond ci-dessus borne la VALEUR remboursable. Celui-ci borne autre
        // chose : ce qui peut sortir en ESPÈCES. Une commande de 13 000 réglée 8 000
        // au prestataire et 5 000 sur la cagnotte n'a fait entrer que 8 000 en caisse.
        //
        // Rembourser 13 000 en Mobile Money n'aurait été ni une perte ni une erreur
        // comptable — la cagnotte dépensée était déjà une dette de la plateforme, et le
        // grand livre restait équilibré. C'eût été une CONVERSION DE DETTE EN SORTIE DE
        // TRÉSORERIE, décidée par un administrateur, majorée des frais de versement du
        // prestataire, sur une somme que le client aurait pu ne jamais retirer.
        //
        // On plafonne donc les espèces à ce qui a été encaissé, et l'excédent retourne
        // en cagnotte. REFUSER cet excédent aurait été plus simple et faux : aucune
        // route ne crédite une cagnotte hors du flux « retour », et une commande
        // entièrement réglée en cagnotte serait devenue non remboursable.
        //
        // `byDirect` ne compte que des remboursements directs, tous en espèces : c'est
        // bien la part espèces déjà rendue qu'il faut soustraire ici.
        // ═════════════════════════════════════════════════════════════════════════
        // `byDirect` ci-dessus somme les DEUX canaux : c'est le plafond de valeur. La
        // répartition, elle, a besoin de la part ESPÈCES déjà rendue — sinon un crédit
        // de cagnotte antérieur consommerait de la caisse qu'il n'a jamais touchée, et
        // le client se verrait refuser un versement auquel il a droit.
        var cashAlreadyRefunded = await _refunds.SumActiveCashForOrderAsync(command.OrderId, cancellationToken);

        var split = RefundSplit.Compute(command.Amount, order.AmountDue, cashAlreadyRefunded);
        var cashPart = split.CashAmount;
        var walletPart = split.WalletAmount;

        CustomerRefundView? cashView = null;

        if (split.RequiresPayout)
        {
            // L'opérateur doit être un Mobile Money routable, sinon le payout échouera —
            // autant refuser avant de débiter la plateforme. Le contrôle a MIGRÉ ici :
            // exigé en tête de méthode, il faisait échouer le remboursement d'une
            // commande entièrement réglée en cagnotte, pour un numéro dont aucune
            // écriture n'aurait eu besoin.
            if (!WalletPayout.IsMobileMoney(command.Provider) || string.IsNullOrWhiteSpace(command.Msisdn))
            {
                return Result.Failure<CustomerRefundSplitView>(Error.Validation(
                    "settlement.provider_unsupported",
                    "Un numéro et un opérateur Mobile Money valides sont requis pour la part remboursée en espèces "
                    + "(mtnmomo, moovmoney, celtis)."));
            }

            var refund = CustomerRefund.CreateCash(
                command.OrderId, order.BuyerId, cashPart, order.Currency, command.Reason, command.Msisdn, command.Provider);
            await _refunds.AddAsync(refund, cancellationToken);

            // Débit plateforme AVANT le versement (contre-passé si le PSP refuse), pour ne
            // jamais laisser un payout parti sans écriture comptable en regard.
            await _wallets.AccrueCustomerRefundAsync(refund.Amount, refund.Currency, refund.Id.Value, cancellationToken);

            // ═════════════════════════════════════════════════════════════════════
            // ON RÉCLAME LA LIGNE AVANT D'APPELER LE PRESTATAIRE. L'ORDRE EST TOUT.
            //
            // Le versement partait auparavant AVANT le moindre `SaveChanges`, et le
            // plafond ne voyait donc rien de ce qui était en vol. Deux conséquences,
            // la seconde pire que la première :
            //
            // 1. CONCURRENCE. Deux administrateurs sur la même commande lisaient tous
            //    deux le même « restant », passaient tous deux le contrôle, et
            //    déclenchaient tous deux le versement.
            //
            // 2. ÉCHEC DE SAUVEGARDE. Le portefeuille plateforme porte un jeton de
            //    concurrence que l'écriture ci-dessus salit. Un conflit sur ce jeton
            //    annulait TOUT — ni ligne de remboursement, ni écriture de grand
            //    livre, ni débit plateforme — alors que l'argent était déjà parti
            //    chez le prestataire. Le plafond ne l'aurait jamais vu, et l'admin
            //    qui relançait sur le 409 en envoyait un troisième.
            //
            // Ce `SaveChanges` fige la ligne en « Processing » AVANT tout appel
            // sortant. La correction existait déjà, à quinze lignes d'ici, sur les
            // retraits ; elle n'avait pas été portée sur ce rail.
            //
            // LE COÛT ASSUMÉ : si le processus meurt entre ce commit et la réponse du
            // prestataire, il reste une ligne « Processing » sans référence PSP. La
            // réconciliation la détecte désormais et alerte — voir
            // `ReconcileCustomerRefundsCommandHandler`.
            // ═════════════════════════════════════════════════════════════════════
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var outcome = await _payouts.SendMobileMoneyPayoutAsync(
                new PayoutInstructionContract(
                    Amount: refund.Amount,
                    Currency: refund.Currency,
                    BeneficiaryName: "Client",
                    Msisdn: refund.Msisdn,
                    Provider: refund.Provider,
                    Reference: $"refund:{refund.Id}"),
                cancellationToken);

            switch (outcome.Status)
            {
                // Rejet DÉFINITIF : rien n'est parti → on contre-passe le débit plateforme.
                //
                // ET ON S'ARRÊTE AVANT LA PART CAGNOTTE. Créditer la cagnotte alors que la
                // part espèces vient d'échouer laisserait un remboursement à moitié fait,
                // que l'administrateur relancerait — et le second passage créditerait la
                // cagnotte une deuxième fois. Tout échoue ensemble, l'admin recommence
                // une fois, et rien n'a bougé entre-temps.
                case PayoutOutcomeStatus.Failed:
                    refund.Fail(outcome.Error ?? "Versement refusé par le prestataire.");
                    await _wallets.ReverseCustomerRefundAsync(refund.Amount, refund.Currency, refund.Id.Value, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    return Result.Failure<CustomerRefundSplitView>(Error.Failure("settlement.refund_failed", refund.FailureReason!));

                // Issue INDÉTERMINÉE (timeout/5xx) : le versement est peut-être parti. On NE
                // contre-passe PAS — la réconciliation tranchera.
                case PayoutOutcomeStatus.Unknown:
                    refund.MarkProcessing(outcome.ProviderReference, outcome.Error);
                    break;

                // Accepté = créé + démarré chez FedaPay (« started », pas encore « sent ») :
                // la réconciliation clôturera en Completed sur le statut « sent ».
                default:
                    refund.MarkProcessing(outcome.ProviderReference);
                    break;
            }

            cashView = WalletMapper.ToView(refund);
        }

        if (walletPart > 0m)
        {
            // ═════════════════════════════════════════════════════════════════════
            // LA JAMBE CAGNOTTE EST UNE LIGNE, PAS SEULEMENT UNE ÉCRITURE.
            //
            // La laisser vivre uniquement dans le grand livre aurait été plus court et
            // faux : le plafond de valeur se calcule en sommant `customer_refunds`, et
            // une jambe absente de cette table est une jambe que le plafond ne voit
            // pas. Deux appels successifs auraient remboursé 18 000 sur une commande à
            // 13 000, chaque contrôle passant proprement — exactement le défaut que
            // §4.1 avait corrigé entre les deux rails, reproduit à l'intérieur d'un
            // seul.
            //
            // La ligne naît « Completed » : rien à attendre d'un prestataire, et la
            // réconciliation ne regarde que les lignes en cours.
            // ═════════════════════════════════════════════════════════════════════
            var walletLeg = CustomerRefund.CreateWallet(
                command.OrderId, order.BuyerId, walletPart, order.Currency, command.Reason);
            await _refunds.AddAsync(walletLeg, cancellationToken);

            // Le coût est comptabilisé côté plateforme comme pour la part espèces :
            // c'est le même remboursement, seul le canal de sortie change.
            await _wallets.AccrueCustomerRefundAsync(walletPart, order.Currency, walletLeg.Id.Value, cancellationToken);
            await _wallets.CreditCustomerFromDirectRefundAsync(
                order.BuyerId, walletPart, order.Currency, walletLeg.Id.Value, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CustomerRefundSplitView(
            command.OrderId, command.Amount, cashPart, walletPart, order.Currency, cashView));
    }
}

/// <summary>
/// Réconcilie les remboursements client « en cours » avec le statut RÉEL du dépôt chez
/// le PSP. Même logique que la réconciliation des retraits : Sent → Completed, Failed →
/// contre-passation du débit plateforme, sinon on ne touche à rien. Idempotent.
/// </summary>
public sealed record ReconcileCustomerRefundsCommand(int BatchSize = 50) : ICommand<int>;

internal sealed class ReconcileCustomerRefundsCommandHandler : ICommandHandler<ReconcileCustomerRefundsCommand, int>
{
    /// <summary>
    /// Au-delà de ce délai, un remboursement « en cours » sans référence PSP n'est plus
    /// une requête en vol : c'est une ligne ÉCHOUÉE, et la plateforme est débitée d'un
    /// argent que le client n'a pas reçu. Même valeur que pour les retraits — deux
    /// seuils différents seraient deux raisonnements à tenir.
    /// </summary>
    private static readonly TimeSpan StrandedAfter = TimeSpan.FromHours(1);

    private readonly ICustomerRefundRepository _refunds;
    private readonly IPayoutModuleApi _payouts;
    private readonly WalletMutations _wallets;
    private readonly ISettlementUnitOfWork _unitOfWork;
    private readonly ILogger<ReconcileCustomerRefundsCommandHandler> _logger;

    public ReconcileCustomerRefundsCommandHandler(
        ICustomerRefundRepository refunds,
        IPayoutModuleApi payouts,
        WalletMutations wallets,
        ISettlementUnitOfWork unitOfWork,
        ILogger<ReconcileCustomerRefundsCommandHandler> logger)
    {
        _refunds = refunds;
        _payouts = payouts;
        _wallets = wallets;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<int>> Handle(ReconcileCustomerRefundsCommand command, CancellationToken cancellationToken)
    {
        // Le lot est borné EN BASE, pas en mémoire. Un `Take` appliqué après la
        // requête matérialisait toutes les lignes bloquées à chaque cycle de deux
        // minutes — et comme le `continue` muet ci-dessous les empêchait de sortir de
        // « Processing », la table de travail ne faisait que grossir.
        var processing = await _refunds.ListProcessingAsync(command.BatchSize, cancellationToken);
        var settled = 0;

        foreach (var refund in processing)
        {
            if (string.IsNullOrWhiteSpace(refund.ProviderRef))
            {
                // ─────────────────────────────────────────────────────────────────
                // CE `continue` ÉTAIT MUET, ET C'EST CE QUI LE RENDAIT DANGEREUX.
                //
                // Sans référence PSP, on ne peut rien interroger ni rembourser à
                // l'aveugle : l'arbitrage reste humain, et sauter la ligne est le bon
                // geste. Mais le faire en silence signifiait que la ligne était
                // repassée et réignorée toutes les deux minutes, indéfiniment, pendant
                // que la plateforme restait débitée et le client non payé — sans une
                // seule alerte, et sans aucun écran qui la montre.
                //
                // Depuis que la ligne est réclamée AVANT l'appel au prestataire, ce
                // cas est devenu ATTENDU : c'est le coût assumé de cet ordre. Il doit
                // donc crier, sinon la correction en aurait créé un autre.
                // ─────────────────────────────────────────────────────────────────
                var age = DateTime.UtcNow - (refund.SentToPspAtUtc ?? refund.CreatedAtUtc);
                if (age > StrandedAfter)
                {
                    _logger.LogCritical(
                        "REMBOURSEMENT IMMOBILISÉ : {RefundId} ({Amount} {Currency}, commande {OrderId}, "
                        + "client {BuyerId}) est « en cours » depuis {Hours:0.0} h SANS référence prestataire. "
                        + "La plateforme est débitée et le client n'est ni payé ni rétabli. Arbitrage manuel requis.",
                        refund.Id.Value, refund.Amount, refund.Currency, refund.OrderId,
                        refund.BuyerId, age.TotalHours);
                }

                continue;
            }

            var progress = await _payouts.GetPayoutProgressAsync(refund.ProviderRef!, cancellationToken);

            switch (progress)
            {
                case PayoutProgress.Sent:
                    refund.Complete(refund.ProviderRef);
                    settled++;
                    break;

                case PayoutProgress.Failed:
                    refund.Fail("Versement refusé par le prestataire (statut « failed »).");
                    await _wallets.ReverseCustomerRefundAsync(refund.Amount, refund.Currency, refund.Id.Value, cancellationToken);
                    settled++;
                    break;

                // pending / started / processing / unknown : encore en vol → on ne touche à rien.
                default:
                    break;
            }
        }

        if (settled > 0)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return settled;
    }
}
