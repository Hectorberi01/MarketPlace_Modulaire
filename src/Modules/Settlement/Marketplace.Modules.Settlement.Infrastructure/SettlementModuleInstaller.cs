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
using Marketplace.Modules.Settlement.Application.Wallets;
using Marketplace.Modules.Settlement.Contracts;
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
        // AUCUN TAUX N'EST LU ICI, ET C'EST LE POINT DE TOUTE LA CORRECTION.
        //
        // Settlement liait auparavant la section de configuration « Pricing » pour
        // répartir le brut d'une ligne. Le barème était donc celui du jour de la
        // CONFIRMATION, alors que le prix payé avait été construit avec celui du jour
        // de la MISE EN VENTE. Tout ajustement de commission réécrivait rétroactivement
        // le net des vendeurs sur l'ensemble de leur catalogue.
        //
        // Les taux voyagent désormais sur la ligne de commande (OrderLineSummary), figés
        // à l'achat. La contre-passation d'un retour, elle, ne lit plus aucun taux : elle
        // reprend au prorata l'écriture d'origine.

        var connectionString = configuration.GetConnectionString("Marketplace")
            ?? throw new InvalidOperationException("Chaîne de connexion « Marketplace » absente.");

        services.AddDbContext<SettlementDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", SettlementDbContext.SchemaName)));

        services.AddScoped<ISettlementUnitOfWork>(sp => sp.GetRequiredService<SettlementDbContext>());

        services.AddScoped<ISellerEarningRepository, SellerEarningRepository>();
        services.AddScoped<ISettlementBatchRepository, SettlementBatchRepository>();

        // Portefeuilles (wallet) : repositories + service de mutation partagé.
        services.AddScoped<IWalletRepository, WalletRepository>();
        services.AddScoped<IWithdrawalRepository, WithdrawalRepository>();
        services.AddScoped<ICustomerRefundRepository, CustomerRefundRepository>();
        services.AddScoped<IWalletTransactionRepository, WalletTransactionRepository>();
        services.AddScoped<WalletMutations>();

        // Coordonnées de versement selon l'acteur : vendeur (Sellers) ou client
        // (Identity). Isolé ici pour que l'arrivée du livreur ne rouvre pas le handler
        // qui décide qu'un versement part.
        services.AddScoped<WithdrawalPayoutResolver>();

        // PREMIÈRE API PUBLIQUE DE SETTLEMENT. Le Saga de commande s'en sert pour
        // engager, consommer ou rendre la cagnotte du client, exactement comme il le
        // fait avec le stock.
        services.AddScoped<ISettlementModuleApi, Public.SettlementModuleApi>();

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

        // REMBOURSEMENT SUR CAGNOTTE : « remboursement validé » crédite désormais le
        // client immédiatement, au lieu d'attendre un versement manuel chez FedaPay.
        services.AddScoped<IIntegrationEventHandler<ReturnRefundApprovedIntegrationEvent>, CreditCustomerWalletOnReturnRefundApprovedHandler>();

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
