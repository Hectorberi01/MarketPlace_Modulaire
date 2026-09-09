using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.BuildingBlocks.Application;
using Marketplace.BuildingBlocks.Infrastructure;
using Marketplace.BuildingBlocks.Infrastructure.Events;
using Marketplace.Modules.Inventory.Contracts;
using Marketplace.Modules.Ordering.Application.Abstractions;
using Marketplace.Modules.Ordering.Domain.Orders;
using Marketplace.Modules.Ordering.Infrastructure;
using Marketplace.Modules.Ordering.Infrastructure.Persistence;
using Marketplace.Modules.Payments.Contracts.IntegrationEvents;
using Marketplace.Modules.Settlement.Contracts;
using Testcontainers.PostgreSql;
using Xunit;

namespace Saga.IntegrationTests;

/// <summary>
/// Test d'intégration du Saga de commande sur un vrai PostgreSQL (Testcontainers,
/// Docker requis). On compose le module Ordering avec un Inventory simulé, puis
/// on pilote le pipeline réel outbox → dispatcher :
///   PaymentCaptured  → la commande passe Confirmed + émet OrderConfirmed à l'outbox
///   PaymentFailed    → la commande passe Cancelled + libère la réservation (compensation)
/// </summary>
public sealed class OrderSagaTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("marketplace")
        .Build();

    private ServiceProvider _services = default!;
    private readonly FakeInventoryModuleApi _inventory = new();
    private readonly FakeSettlementModuleApi _settlement = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Marketplace"] = _postgres.GetConnectionString(),
                // Clé de test : le module Identity refuse de s'installer sans elle.
                ["Jwt:SigningKey"] = "test-only-signing-key-at-least-32-bytes-long!!"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        var installer = new OrderingModuleInstaller();
        installer.Install(services, configuration);

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(installer.ApplicationAssembly));
        services.AddBuildingBlocksPipeline();
        services.AddBuildingBlocksInfrastructure();

        // Inventory simulé : la saga reste pilotée par le module Ordering réel.
        services.AddScoped<IInventoryModuleApi>(_ => _inventory);

        // Settlement simulé, pour la même raison : la saga engage et compense désormais
        // la cagnotte du client au même titre que le stock.
        services.AddScoped<ISettlementModuleApi>(_ => _settlement);

        _services = services.BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task PaymentCaptured_confirme_la_commande_et_emet_OrderConfirmed()
    {
        var orderId = await SeedAwaitingPaymentOrderAsync();

        await DispatchAsync(new PaymentCapturedIntegrationEvent { PaymentId = Guid.NewGuid(), OrderId = orderId });

        await using var scope = _services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        var order = await dbContext.Orders.FirstAsync(o => o.Id == new OrderId(orderId));
        order.Status.Should().Be(OrderStatus.Confirmed);

        _inventory.Confirmed.Should().Contain(c => c.OrderId == orderId);

        var outboxTypes = await dbContext.OutboxMessages.Select(m => m.Type).ToListAsync();
        outboxTypes.Should().Contain(t => t.Contains("OrderConfirmedIntegrationEvent"));
    }

    [Fact]
    public async Task PaymentFailed_annule_la_commande_et_libere_la_reservation()
    {
        var orderId = await SeedAwaitingPaymentOrderAsync();

        await DispatchAsync(new PaymentFailedIntegrationEvent
        {
            PaymentId = Guid.NewGuid(),
            OrderId = orderId,
            Reason = "Carte refusée"
        });

        await using var scope = _services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        var order = await dbContext.Orders.FirstAsync(o => o.Id == new OrderId(orderId));
        order.Status.Should().Be(OrderStatus.Cancelled);

        _inventory.Released.Should().Contain(r => r.OrderId == orderId);

        var outboxTypes = await dbContext.OutboxMessages.Select(m => m.Type).ToListAsync();
        outboxTypes.Should().Contain(t => t.Contains("OrderCancelledIntegrationEvent"));
    }

    [Fact]
    public async Task PaymentCaptured_consomme_la_cagnotte_engagee_sur_la_commande()
    {
        var orderId = await SeedAwaitingPaymentOrderAsync(walletApplied: 6_000m);

        await DispatchAsync(new PaymentCapturedIntegrationEvent { PaymentId = Guid.NewGuid(), OrderId = orderId });

        // La part réglée par la cagnotte quitte le réservé pour de bon, exactement pour
        // le montant figé sur la commande — pas pour ce que le client avait demandé.
        _settlement.Confirmed.Should().ContainSingle(c => c.OrderId == orderId && c.Amount == 6_000m);
        _settlement.Released.Should().NotContain(r => r.OrderId == orderId);
    }

    [Fact]
    public async Task PaymentFailed_rend_la_cagnotte_engagee_au_client()
    {
        var orderId = await SeedAwaitingPaymentOrderAsync(walletApplied: 6_000m);

        await DispatchAsync(new PaymentFailedIntegrationEvent
        {
            PaymentId = Guid.NewGuid(),
            OrderId = orderId,
            Reason = "Solde insuffisant"
        });

        // SANS CETTE COMPENSATION, l'argent resterait bloqué dans le réservé : ni
        // dépensable, ni retirable, ni visible — et aucun écran ne pourrait dire au
        // client où il est passé.
        _settlement.Released.Should().ContainSingle(r => r.OrderId == orderId && r.Amount == 6_000m);
        _settlement.Confirmed.Should().NotContain(c => c.OrderId == orderId);
    }

    /// <summary>Crée une commande en « attente de paiement » (état d'entrée du Saga).</summary>
    private async Task<Guid> SeedAwaitingPaymentOrderAsync(decimal walletApplied = 0m)
    {
        await using var scope = _services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IOrderingUnitOfWork>();

        var draft = new OrderLineDraft(
            OfferId: Guid.NewGuid(),
            ProductId: Guid.NewGuid(),
            SellerId: Guid.NewGuid(),
            Sku: "ROBE-WAX-M",
            ShipFromLocationId: Guid.NewGuid(),
            Quantity: 2,
            UnitBasePrice: 10_000m,
            SellerDiscount: 0m,
            PlatformDiscount: 0m,
            // Montant de LIGNE, pas prix unitaire : deux articles à 10 000.
            LineTotal: 20_000m,
            CommissionRate: 0.10m,
            ProviderFeeRate: 0.05m);

        var order = Order.Create(Guid.NewGuid(), Guid.NewGuid(), "XOF", new[] { draft }).Value;

        // La cagnotte s'impute AVANT la mise en attente de paiement : après, le montant
        // annoncé au prestataire ne bouge plus (invariant du domaine).
        order.ApplyWalletCredit(walletApplied).IsSuccess.Should().BeTrue();
        order.MarkAwaitingPayment().IsSuccess.Should().BeTrue();

        await repository.AddAsync(order);
        await unitOfWork.SaveChangesAsync();

        return order.Id.Value;
    }

    /// <summary>Joue le dispatch in-process d'un event d'intégration (équivalent au pump de l'outbox).</summary>
    private async Task DispatchAsync(Marketplace.BuildingBlocks.IntegrationEvents.IntegrationEvent integrationEvent)
    {
        await using var scope = _services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IntegrationEventDispatcher>();
        await dispatcher.DispatchAsync(integrationEvent);
    }
}
