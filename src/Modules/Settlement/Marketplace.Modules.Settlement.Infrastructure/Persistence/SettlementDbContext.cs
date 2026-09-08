using Microsoft.EntityFrameworkCore;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.Infrastructure.Persistence;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Domain.Batches;
using Marketplace.Modules.Settlement.Domain.Earnings;
using Marketplace.Modules.Settlement.Domain.Wallets;

namespace Marketplace.Modules.Settlement.Infrastructure.Persistence;

/// <summary>DbContext du module Settlement (schéma « settlement »).</summary>
public sealed class SettlementDbContext : ModuleDbContext, ISettlementUnitOfWork
{
    public const string SchemaName = "settlement";

    public SettlementDbContext(
        DbContextOptions<SettlementDbContext> options,
        IDomainEventDispatcher domainEventDispatcher,
        IntegrationEventQueue integrationEventQueue)
        : base(options, domainEventDispatcher, integrationEventQueue)
    {
    }

    public DbSet<SellerEarning> Earnings => Set<SellerEarning>();
    public DbSet<SettlementBatch> Batches => Set<SettlementBatch>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletAccountBalance> WalletAccounts => Set<WalletAccountBalance>();
    public DbSet<Withdrawal> Withdrawals => Set<Withdrawal>();
    public DbSet<CustomerRefund> CustomerRefunds => Set<CustomerRefund>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();

    protected override string Schema => SchemaName;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SettlementDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
