using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Infrastructure.Persistence;

internal sealed class WalletRepository : IWalletRepository
{
    private readonly SettlementDbContext _dbContext;

    public WalletRepository(SettlementDbContext dbContext) => _dbContext = dbContext;

    // SUIVI EF ACTIVÉ, ET LES SOLDES CHARGÉS AVEC. Un portefeuille n'est jamais lu pour
    // être seulement regardé : il est lu pour être modifié, et modifier un solde exige
    // que la collection d'entités enfants soit là. Un Include oublié ici donnerait un
    // agrégat aux comptes vides, donc des crédits silencieusement perdus.
    public async Task<Wallet?> GetAsync(
        WalletOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default)
        => await _dbContext.Wallets
            .Include(w => w.Accounts)
            .FirstOrDefaultAsync(w => w.OwnerType == ownerType && w.OwnerId == ownerId, cancellationToken);

    public async Task<Wallet?> GetByIdAsync(WalletId id, CancellationToken cancellationToken = default)
        => await _dbContext.Wallets
            .Include(w => w.Accounts)
            .FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public Task<Wallet?> GetPlatformAsync(CancellationToken cancellationToken = default)
        => GetAsync(WalletOwnerType.Platform, Wallet.PlatformOwnerId, cancellationToken);

    public async Task AddAsync(Wallet wallet, CancellationToken cancellationToken = default)
        => await _dbContext.Wallets.AddAsync(wallet, cancellationToken);

    // SANS SUIVI, exception à la règle du dessus : c'est la seule lecture de
    // portefeuilles qui ne débouche pas sur une écriture. Le contrôle de réconciliation
    // regarde, il ne touche à rien — et charger des milliers d'agrégats dans le change
    // tracker ferait payer très cher un simple constat.
    public async Task<IReadOnlyList<Wallet>> ListAllAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Wallets
            .AsNoTracking()
            .Include(w => w.Accounts)
            .OrderBy(w => w.OwnerType)
            .ThenBy(w => w.OwnerId)
            .ToListAsync(cancellationToken);
}

internal sealed class WithdrawalRepository : IWithdrawalRepository
{
    private readonly SettlementDbContext _dbContext;

    public WithdrawalRepository(SettlementDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Withdrawal withdrawal, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals.AddAsync(withdrawal, cancellationToken);

    public async Task<IReadOnlyList<Withdrawal>> ListByOwnerAsync(
        WalletOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals
            .AsNoTracking()
            .Where(w => w.OwnerType == ownerType && w.OwnerId == ownerId)
            .OrderByDescending(w => w.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    // Suivi activé : l'entité est mutée puis persistée (validation/refus admin).
    public async Task<Withdrawal?> GetByIdAsync(WithdrawalId id, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Withdrawal>> ListByStatusAsync(
        WithdrawalStatus status, WalletOwnerType? ownerType = null, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals
            .AsNoTracking()
            .Where(w => w.Status == status && (ownerType == null || w.OwnerType == ownerType))
            .OrderBy(w => w.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    // SUIVI activé (pas de AsNoTracking) : la réconciliation mute ces entités.
    public async Task<IReadOnlyList<Withdrawal>> ListProcessingForReconciliationAsync(int take, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals
            .Where(w => w.Status == WithdrawalStatus.Processing)
            .OrderBy(w => w.CreatedAtUtc)
            .Take(take <= 0 ? 50 : take)
            .ToListAsync(cancellationToken);

    // SUIVI activé : le webhook mute l'entité juste après la corrélation.
    public async Task<Withdrawal?> GetByProviderRefAsync(string providerRef, CancellationToken cancellationToken = default)
        => await _dbContext.Withdrawals
            .FirstOrDefaultAsync(w => w.ProviderRef == providerRef, cancellationToken);
}

internal sealed class CustomerRefundRepository : ICustomerRefundRepository
{
    // Bornes propres à ce dépôt. `WalletTransactionRepository` en a de son côté :
    // les partager exigerait un type commun pour deux constantes, et le couplage
    // coûterait plus que la répétition.
    private const int DefaultBatchSize = 50;
    private const int MaxBatchSize = 200;

    private readonly SettlementDbContext _dbContext;

    public CustomerRefundRepository(SettlementDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(CustomerRefund refund, CancellationToken cancellationToken = default)
        => await _dbContext.CustomerRefunds.AddAsync(refund, cancellationToken);

    // Suivi activé : l'entité est mutée puis persistée (réconciliation).
    public async Task<CustomerRefund?> GetByIdAsync(CustomerRefundId id, CancellationToken cancellationToken = default)
        => await _dbContext.CustomerRefunds.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<decimal> SumActiveForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.CustomerRefunds
            .Where(r => r.OrderId == orderId && r.Status != CustomerRefundStatus.Failed)
            .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;

    public async Task<decimal> SumActiveCashForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => await _dbContext.CustomerRefunds
            .Where(r => r.OrderId == orderId
                        && r.Status != CustomerRefundStatus.Failed
                        && r.Channel == CustomerRefundChannel.Cash)
            .SumAsync(r => (decimal?)r.Amount, cancellationToken) ?? 0m;

    // SUIVI activé (pas de AsNoTracking) : la réconciliation mute ces entités.
    //
    // Le filtre sur le canal est REDONDANT — une jambe cagnotte naît « Completed » et ne
    // peut donc pas apparaître ici. Il est explicite quand même : c'est la garantie que
    // la réconciliation n'ira jamais interroger FedaPay sur un versement qui n'a jamais
    // existé, y compris si quelqu'un fait un jour repasser une ligne en « Processing ».
    public async Task<IReadOnlyList<CustomerRefund>> ListProcessingAsync(int take, CancellationToken cancellationToken = default)
        => await _dbContext.CustomerRefunds
            .Where(r => r.Status == CustomerRefundStatus.Processing
                        && r.Channel == CustomerRefundChannel.Cash)
            .OrderBy(r => r.CreatedAtUtc)
            .Take(Math.Clamp(take <= 0 ? DefaultBatchSize : take, 1, MaxBatchSize))
            .ToListAsync(cancellationToken);
}

internal sealed class WalletTransactionRepository : IWalletTransactionRepository
{
    private readonly SettlementDbContext _dbContext;

    public WalletTransactionRepository(SettlementDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(WalletTransaction transaction, CancellationToken cancellationToken = default)
        => await _dbContext.WalletTransactions.AddAsync(transaction, cancellationToken);

    /// <summary>Nombre de lignes rendues quand l'appelant n'en demande pas explicitement.</summary>
    private const int DefaultPageSize = 50;

    /// <summary>
    /// Plafond dur. Un relevé de cagnotte se lit par pages dans l'application ; personne
    /// n'a besoin de plus en une fois, et l'appelant qui en demande plus ne rend service
    /// à personne, à commencer par lui-même.
    /// </summary>
    private const int MaxPageSize = 200;

    public async Task<IReadOnlyList<WalletTransaction>> ListByOwnerAsync(
        WalletOwnerType ownerType, Guid ownerId, int take, CancellationToken cancellationToken = default)
        => await _dbContext.WalletTransactions
            .AsNoTracking()
            .Where(t => t.OwnerType == ownerType && t.OwnerId == ownerId)
            .OrderByDescending(t => t.CreatedAtUtc)
            // MÊME DÉPARTAGE QUE `ListInPeriodAsync`, ET POUR UNE RAISON DE PLUS.
            //
            // Deux écritures posées dans la même transaction portent la même date à la
            // microseconde près : sans second critère, leur ordre relatif change d'un
            // appel à l'autre. Ici cela ne faisait que réordonner deux lignes voisines
            // du tableau — mais la console s'appuie désormais sur le DERNIER élément
            // reçu pour savoir si sa fenêtre est complète (courbe du portefeuille). Au
            // 200ᵉ rang, « le plus ancien reçu » devenait alors une valeur instable, et
            // avec elle la preuve de complétude.
            .ThenByDescending(t => t.Id)
            // BORNÉ DES DEUX CÔTÉS. Le plancher existait, le PLAFOND non : les quatre
            // BFF passent la chaîne de requête telle quelle, donc
            // `?take=100000000` matérialisait tout le grand livre de l'appelant. Pas une
            // divulgation — le filtre `OwnerId` tient — mais un coût serveur laissé au
            // choix du client.
            //
            // Le plafond est ICI plutôt que dans chaque endpoint : c'est le seul endroit
            // que les quatre hôtes traversent, donc le seul qu'on ne peut pas oublier en
            // ajoutant une cinquième surface.
            .Take(Math.Clamp(take <= 0 ? DefaultPageSize : take, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WalletTransaction>> ListInPeriodAsync(
        WalletOwnerType ownerType, Guid ownerId, DateTime fromUtc, DateTime toUtc,
        int take, CancellationToken cancellationToken = default)
        => await _dbContext.WalletTransactions
            .AsNoTracking()
            .Where(t => t.OwnerType == ownerType
                && t.OwnerId == ownerId
                && t.CreatedAtUtc >= fromUtc
                && t.CreatedAtUtc < toUtc)
            .OrderByDescending(t => t.CreatedAtUtc)
            // Départage les écritures d'un même instant : deux lignes posées dans la
            // même transaction portent la même date à la microseconde près, et sans ce
            // second critère leur ordre change d'un affichage à l'autre.
            .ThenByDescending(t => t.Id)
            .Take(Math.Clamp(take <= 0 ? DefaultPageSize : take, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

    public async Task<LedgerPeriodTotal> SumInPeriodAsync(
        WalletOwnerType ownerType, Guid ownerId, string referenceType,
        WalletDirection direction, DateTime fromUtc, DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var lignes = _dbContext.WalletTransactions
            .AsNoTracking()
            .Where(t => t.OwnerType == ownerType
                && t.OwnerId == ownerId
                && t.ReferenceType == referenceType
                && t.Direction == direction
                && t.CreatedAtUtc >= fromUtc
                && t.CreatedAtUtc < toUtc);

        // ─────────────────────────────────────────────────────────────────────────
        // DEUX AGRÉGATS PLUTÔT QU'UN `GroupBy` SUR CONSTANTE.
        //
        // La version en une requête regroupait sur `_ => 1` pour ramener somme et
        // décompte ensemble. Elle aurait sans doute fonctionné — mais ce serait le SEUL
        // regroupement sur constante du dépôt, donc le seul dont la traduction ne
        // s'appuie sur aucun précédent en production, et un échec de traduction n'est
        // pas une valeur fausse : c'est un 500 sur l'écran.
        //
        // Deux allers-retours, donc, sur deux formes dont la traduction n'est douteuse
        // nulle part. Le risque d'incohérence entre les deux lectures est nul en
        // pratique : le grand livre est en AJOUT SEUL, une écriture concurrente ne peut
        // qu'apparaître à la fin, jamais rendre un montant sans son décompte.
        //
        // Le `(decimal?)` n'est pas décoratif : sans lui, `SumAsync` sur un ensemble
        // vide matérialise un NULL SQL dans un `decimal` et lève.
        // ─────────────────────────────────────────────────────────────────────────
        var montant = await lignes.SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;
        var nombre = await lignes.CountAsync(cancellationToken);

        return new LedgerPeriodTotal(montant, nombre);
    }

    /// <summary>
    /// Le grand livre a-t-il déjà une écriture pour cette référence ?
    ///
    /// On interroge le CHANGE TRACKER autant que la base (pas d'AsNoTracking, et
    /// `Local` d'abord) : dans une même transaction, les écritures précédentes ne sont
    /// pas encore commitées. Sans cela, deux handlers du même message se croiraient
    /// tous deux « premiers ».
    /// </summary>
    public async Task<bool> ExistsForReferenceAsync(
        string referenceType, Guid referenceId, CancellationToken cancellationToken = default)
    {
        var pending = _dbContext.WalletTransactions.Local
            .Any(t => t.ReferenceType == referenceType && t.ReferenceId == referenceId);

        if (pending)
        {
            return true;
        }

        return await _dbContext.WalletTransactions
            .AnyAsync(t => t.ReferenceType == referenceType && t.ReferenceId == referenceId, cancellationToken);
    }

    /// <summary>
    /// Somme signée des écritures d'un compte. Calculée EN BASE, pas en mémoire : un
    /// portefeuille actif accumule des milliers de lignes, et ce contrôle a vocation à
    /// tourner sur l'ensemble des portefeuilles.
    /// </summary>
    public async Task<decimal> SumForAccountAsync(
        Guid ownerId, WalletOwnerType ownerType, WalletAccount account, CancellationToken cancellationToken = default)
    {
        var rows = _dbContext.WalletTransactions
            .Where(t => t.OwnerId == ownerId && t.OwnerType == ownerType && t.Account == account);

        var credits = await rows.Where(t => t.Direction == WalletDirection.Credit)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        var debits = await rows.Where(t => t.Direction == WalletDirection.Debit)
            .SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0m;

        return credits - debits;
    }

    /// <summary>
    /// Tous les soldes du grand livre en UNE requête : un GROUP BY sur (propriétaire,
    /// type, compte), le débit comptant en négatif.
    ///
    /// La somme signée est obtenue par un CASE dans l'agrégat plutôt que par deux
    /// passes crédits/débits : une seule lecture de l'index, et surtout un résultat
    /// complet — un compte qui n'aurait que des débits sort quand même, ce qu'une
    /// jointure entre deux agrégats aurait laissé filer.
    /// </summary>
    public async Task<IReadOnlyList<LedgerAccountSum>> SumAllAccountsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _dbContext.WalletTransactions
            .AsNoTracking()
            .GroupBy(t => new { t.OwnerId, t.OwnerType, t.Account })
            .Select(g => new
            {
                g.Key.OwnerId,
                g.Key.OwnerType,
                g.Key.Account,
                Sum = g.Sum(t => t.Direction == WalletDirection.Credit ? t.Amount : -t.Amount)
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new LedgerAccountSum(r.OwnerId, r.OwnerType, r.Account, r.Sum))
            .ToList();
    }
}
