using System.Reflection;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Domain.Events;
using Marketplace.BuildingBlocks.Infrastructure.Modularity;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Ordering.Contracts.IntegrationEvents;
using Marketplace.Modules.Returns.Contracts.IntegrationEvents;
using Marketplace.Modules.Shipping.Contracts.IntegrationEvents;
using Marketplace.Modules.Settlement.Application.Abstractions;
using Marketplace.Modules.Settlement.Application.Batches;
using Marketplace.Modules.Settlement.Application.Batches.EventHandlers;
using Marketplace.Modules.Settlement.Application.Earnings;
using Marketplace.Modules.Settlement.Application.Pricing;
using Marketplace.Modules.Settlement.Application.Wallets;
using Marketplace.Modules.Settlement.Domain.Batches;
using Marketplace.Modules.Settlement.Domain.Batches.Events;
using Marketplace.Modules.Settlement.Domain.Earnings;
using Marketplace.Modules.Settlement.Domain.Wallets;
using Marketplace.Modules.Settlement.Infrastructure.Persistence;

namespace Marketplace.Modules.Settlement.Infrastructure;

/// <summary>Enregistre le module Settlement : DbContext, repositories, accrual consumer, validators, outbox.</summary>
public sealed class SettlementModuleInstaller : IModuleInstaller
{
    public string ModuleName => "Settlement";

    public Assembly ApplicationAssembly => typeof(RunSettlementCommand).Assembly;

    public void Install(IServiceCollection services, IConfiguration configuration)
    {
        // Taux de tarification (section « Pricing »), liés manuellement (même
        // pattern que Billing : aucune dépendance IOptions). Servent à répartir
        // le brut d'une ligne entre net vendeur, commission et frais provider.
        var pricingOptions = new PricingOptions();
        var commissionRateRaw = configuration.GetSection("Pricing")["PlatformCommissionRate"];
        if (decimal.TryParse(commissionRateRaw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedCommissionRate))
        {
            pricingOptions.PlatformCommissionRate = parsedCommissionRate;
        }

        var providerRateRaw = configuration.GetSection("Pricing")["ProviderFeeRate"];
        if (decimal.TryParse(providerRateRaw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedProviderRate))
        {
            pricingOptions.ProviderFeeRate = parsedProviderRate;
        }

        services.AddSingleton(pricingOptions);

        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<SettlementDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", SettlementDbContext.SchemaName)));

        services.AddScoped<ISettlementUnitOfWork>(sp => sp.GetRequiredService<SettlementDbContext>());

        services.AddScoped<ISellerEarningRepository, SellerEarningRepository>();
        services.AddScoped<ISettlementBatchRepository, SettlementBatchRepository>();

        // Portefeuilles (wallet) : repositories + service de mutation partagé.
        services.AddScoped<ISellerWalletRepository, SellerWalletRepository>();
        services.AddScoped<IPlatformWalletRepository, PlatformWalletRepository>();
        services.AddScoped<IWithdrawalRepository, WithdrawalRepository>();
        services.AddScoped<IWalletTransactionRepository, WalletTransactionRepository>();
        services.AddScoped<WalletMutations>();

        // Règle de clôture d'un retrait (sent → payé, failed → remboursé), partagée par
        // le webhook et la réconciliation : une seule et même logique, donc pas de risque
        // qu'un double verdict produise un double remboursement.
        services.AddScoped<WithdrawalSettlement>();

        services.AddScoped<IDomainEventHandler<PayoutPaidDomainEvent>, PayoutPaidDomainEventHandler>();

        // Réconciliation des retraits « en cours » avec le statut réel du PSP.
        // SANS ce service, un versement échoué chez FedaPay laisserait le retrait
        // marqué « payé » et le vendeur débité, sans jamais avoir reçu son argent.
        services.AddHostedService<Reconciliation.WithdrawalReconciliationService>();

        // Chorégraphie : alimentation du grand livre des gains à la confirmation de commande.
        services.AddScoped<IIntegrationEventHandler<OrderConfirmedIntegrationEvent>, AccrueEarningsOnOrderConfirmedHandler>();

        // CONTRE-PASSATION. Sans ce handler, l'événement « retour remboursé » était
        // publié dans le vide : le vendeur gardait son gain sur un article qui nous
        // revenait, et la plateforme payait deux fois — le client ET le vendeur.
        services.AddScoped<IIntegrationEventHandler<ReturnRefundedIntegrationEvent>, ReverseEarningsOnReturnRefundedHandler>();

        // Libération des gains (escrow levé) à la livraison confirmée → payables.
        services.AddScoped<IIntegrationEventHandler<OrderDeliveredIntegrationEvent>, ReleaseEarningsOnOrderDeliveredHandler>();

        // Affinage multi-vendeur : libération des gains d'un vendeur dès SA livraison.
        services.AddScoped<IIntegrationEventHandler<ShipmentDeliveredIntegrationEvent>, ReleaseSellerEarningsOnShipmentDeliveredHandler>();

        services.AddValidatorsFromAssembly(ApplicationAssembly, includeInternalTypes: true);

        services.AddOutboxProcessor<SettlementDbContext>();
    }
}
