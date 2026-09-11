using FluentValidation;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Delivery.Contracts;
using Marketplace.Modules.Settlement.Contracts;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Application.Wallets;

/// <summary>
/// LE RELEVÉ D'UN COURSIER SUR UNE PÉRIODE.
///
/// ═════════════════════════════════════════════════════════════════════════════════
/// CE QUE C'EST, ET SURTOUT CE QUE CE N'EST PAS.
///
/// Ce n'est PAS un lot de règlement. Le vendeur est réglé par lots parce que son gain
/// est retenu jusqu'à la livraison puis reversé par période : le lot est l'acte qui
/// sort l'argent. Le coursier, lui, est payé À LA COURSE — le crédit tombe sur son
/// disponible à l'instant où la course est livrée, et il retire quand il veut. Il n'y a
/// donc rien à « lancer » pour lui, et un écran qui proposerait de le faire mentirait
/// sur le fonctionnement du système.
///
/// Ce relevé RÉPOND À CE QUI MANQUAIT VRAIMENT : jusqu'ici, un administrateur ne voyait
/// d'un coursier que ses demandes de retrait. Ni son solde, ni ce qu'il a gagné, ni en
/// combien de courses. Il approuvait un versement sans pouvoir répondre à la seule
/// question qui compte — « cet argent, d'où vient-il ? ».
///
/// AUCUNE TABLE NOUVELLE. Tout se lit du GRAND LIVRE, qui porte déjà chaque mouvement
/// avec son sens, son motif et sa référence d'origine. Créer une table de gains
/// coursier pour la remplir des mêmes lignes, c'est se donner deux vérités à tenir
/// d'accord — et le grand livre, lui, est déjà réconcilié contre les soldes.
///
/// LE JOUR OÙ UN LOT COURSIER DEVIENDRA NÉCESSAIRE, la forme est arrêtée : `Payout`
/// gagnera un `OwnerType` et `SellerId` deviendra `OwnerId`, plutôt qu'un second
/// agrégat parallèle. Ce relevé n'y fait pas obstacle — il ne pose ni table ni statut
/// qu'il faudrait ensuite défaire.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
/// <param name="FromUtc">Borne INCLUSE.</param>
/// <param name="ToUtc">
/// Borne EXCLUE. La convention du module, et la seule qui rende deux périodes contiguës
/// sans recouvrement ni trou : une course livrée à minuit pile appartient au jour qui
/// commence, jamais aux deux.
/// </param>
public sealed record GetCourierStatementQuery(Guid CourierId, DateTime FromUtc, DateTime ToUtc)
    : IQuery<CourierStatementView>;

public sealed class GetCourierStatementQueryValidator : AbstractValidator<GetCourierStatementQuery>
{
    /// <summary>
    /// Plafond de l'étendue demandable.
    ///
    /// Les lignes sont bornées par le dépôt, mais les DEUX AGRÉGATS ne le sont pas : ils
    /// balaient la période entière. Sans ce garde-fou, « du 01/01/1900 à aujourd'hui »
    /// est une requête valide qui parcourt tout le grand livre du coursier. Un an
    /// dépasse largement tout usage réel — on règle un coursier au mois, on l'audite au
    /// trimestre.
    /// </summary>
    private const int EtendueMaxJours = 366;

    public GetCourierStatementQueryValidator()
    {
        RuleFor(q => q.CourierId).NotEmpty();

        RuleFor(q => q.ToUtc)
            .GreaterThan(q => q.FromUtc)
            .WithMessage("La fin de période doit être postérieure à son début.");

        RuleFor(q => q)
            .Must(q => (q.ToUtc - q.FromUtc).TotalDays <= EtendueMaxJours)
            .WithMessage($"La période ne peut pas dépasser {EtendueMaxJours} jours.");
    }
}

internal sealed class GetCourierStatementQueryHandler
    : IQueryHandler<GetCourierStatementQuery, CourierStatementView>
{
    /// <summary>
    /// Lignes rendues au plus. Le dépôt plafonne déjà à 200 ; cette constante dit la
    /// valeur DEMANDÉE, pour que la troncature annoncée à l'écran soit calculée sur le
    /// même nombre que celui qui l'a produite.
    /// </summary>
    private const int MaxLignes = 150;

    private readonly IWalletRepository _wallets;
    private readonly IWithdrawalRepository _withdrawals;
    private readonly IWalletTransactionRepository _ledger;
    private readonly IDeliveryModuleApi _couriers;

    public GetCourierStatementQueryHandler(
        IWalletRepository wallets,
        IWithdrawalRepository withdrawals,
        IWalletTransactionRepository ledger,
        IDeliveryModuleApi couriers)
    {
        _wallets = wallets;
        _withdrawals = withdrawals;
        _ledger = ledger;
        _couriers = couriers;
    }

    public async Task<Result<CourierStatementView>> Handle(
        GetCourierStatementQuery query, CancellationToken cancellationToken)
    {
        var courier = await _couriers.GetCourierAsync(query.CourierId, cancellationToken);
        if (courier is null)
        {
            return Result.Failure<CourierStatementView>(
                Error.NotFound("settlement.courier_not_found", "Coursier introuvable."));
        }

        var wallet = await _wallets.GetAsync(WalletOwnerType.Courier, query.CourierId, cancellationToken);

        // ─────────────────────────────────────────────────────────────────────────
        // CE QU'IL A GAGNÉ SE LIT AU GRAND LIVRE. CE QU'IL A TOUCHÉ, NON.
        //
        // Un solde ne dit pas d'où il vient : deux coursiers au même solde peuvent avoir
        // l'un beaucoup gagné et beaucoup retiré, l'autre presque rien fait — et c'est
        // le premier qu'on veut voir travailler.
        //
        // Le GAIN se lit bien au grand livre : chaque course livrée y pose un crédit
        // « courier_delivery », et rien d'autre ne l'écrit.
        //
        // LE RETRAIT, LUI, NE S'Y LIT PAS. Le débit « withdrawal » est posé à la DEMANDE,
        // pas au versement : les fonds sont retenus dès que le coursier clique, et rendus
        // par un crédit de même référence si la demande est refusée ou si le versement
        // échoue. Sommer ces débits annoncerait donc « retiré » un argent jamais parti —
        // et le compterait une seconde fois dans « retrait en cours », qui porte
        // exactement les mêmes demandes. Deux cartes côte à côte, le même argent, et
        // l'une des deux forcément fausse.
        //
        // On lit donc les retraits CLÔTURÉS, par leur date de clôture. C'est la seule
        // lecture qui réponde à « combien cet homme a-t-il touché sur la période ».
        // ─────────────────────────────────────────────────────────────────────────
        var gagne = await _ledger.SumInPeriodAsync(
            WalletOwnerType.Courier, query.CourierId,
            WalletMutations.CourierDeliveryReferenceType, WalletDirection.Credit,
            query.FromUtc, query.ToUtc, cancellationToken);

        // UNE lecture des retraits, DEUX chiffres. Un coursier en compte quelques
        // dizaines sur toute sa vie : les filtrer en mémoire coûte moins qu'un
        // aller-retour de plus, et évite deux vues d'un même ensemble prises à deux
        // instants différents.
        var retraits = await _withdrawals.ListByOwnerAsync(
            WalletOwnerType.Courier, query.CourierId, cancellationToken);

        var clotures = retraits
            .Where(w => w.Status == WithdrawalStatus.Completed
                && w.CompletedAtUtc is { } fin
                && fin >= query.FromUtc
                && fin < query.ToUtc)
            .ToList();

        var pendingWithdrawal = retraits
            .Where(w => w.Status is WithdrawalStatus.Requested or WithdrawalStatus.Processing)
            .Sum(w => w.Amount);

        // UNE LIGNE DE PLUS QUE CE QU'ON AFFICHE, et c'est tout l'intérêt : demander
        // exactement le plafond rend impossible de distinguer « pile 150 mouvements » de
        // « au moins 150 ». L'écran annonçait alors une troncature inexistante, et un
        // avertissement qu'on prend en défaut est un avertissement qu'on cesse de lire.
        var lues = await _ledger.ListInPeriodAsync(
            WalletOwnerType.Courier, query.CourierId,
            query.FromUtc, query.ToUtc, MaxLignes + 1, cancellationToken);

        var tronque = lues.Count > MaxLignes;
        var lignes = tronque ? lues.Take(MaxLignes).ToList() : lues;

        return new CourierStatementView(
            CourierId: query.CourierId,
            DisplayName: courier.DisplayName,
            Phone: courier.Phone,
            CourierStatus: courier.Status,
            // Recopié tel quel du module Delivery, jamais redéduit ici : c'est LUI qui
            // décide si un coursier est payable, et la seule règle qui vaille est la
            // sienne.
            CanBePaid: courier.CanBePaid,
            PayoutProvider: courier.Payout?.Provider,
            PayoutAccountNumber: courier.Payout?.AccountNumber,
            PeriodStartUtc: query.FromUtc,
            PeriodEndUtc: query.ToUtc,
            EarnedInPeriod: gagne.Amount,
            DeliveriesInPeriod: gagne.Count,
            WithdrawnInPeriod: clotures.Sum(w => w.Amount),
            WithdrawalsInPeriod: clotures.Count,
            PendingBalance: wallet?.Balance(WalletAccount.Pending) ?? 0m,
            AvailableBalance: wallet?.Balance(WalletAccount.Available) ?? 0m,
            PendingWithdrawal: pendingWithdrawal,
            Currency: wallet?.Currency ?? "XOF",
            Lines: lignes.Select(WalletMapper.ToView).ToList(),
            // La troncature est ANNONCÉE, pas subie. Un relevé coupé ressemble trait pour
            // trait à un relevé complet : sans ce drapeau, l'écran affiche ce qu'il a reçu
            // et laisse croire que c'est tout. Le cumul, lui, reste juste — il est agrégé
            // en base, pas déduit de cette liste.
            LinesTruncated: tronque);
    }
}
