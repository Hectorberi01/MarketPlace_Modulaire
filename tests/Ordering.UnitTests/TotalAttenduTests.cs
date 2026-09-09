using FluentAssertions;
using Marketplace.Modules.Cart.Contracts;
using Marketplace.Modules.Inventory.Contracts;
using Marketplace.Modules.Ordering.Application.Abstractions;
using Marketplace.Modules.Ordering.Application.Orders.Commands.PlaceOrder;
using Marketplace.Modules.Ordering.Domain.Orders;
using Marketplace.Modules.Settlement.Contracts;
using Marketplace.Modules.Tax.Contracts;
using Xunit;

namespace Ordering.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// LE PRIX AFFICHÉ ENGAGE. NE PAS ASSOUPLIR EN TOLÉRANCE.
///
/// `/checkout/quote` valorise le panier à un instant, `/checkout/pay` le revalorise à
/// un autre — et `PriceCalculator` réévalue au passage si les promotions sont encore
/// actives. Rien ne rapprochait les deux valorisations.
///
/// Un acheteur ouvrant son paiement à 23 h 58 avec une promotion expirant à minuit, et
/// appuyant sur « Payer » à 00 h 01, voyait 11 500 et se faisait débiter 13 000 : la
/// commande était figée au nouveau prix et le prestataire réclamait celui-là. Rien dans
/// la requête ne portait le prix annoncé, donc rien ne pouvait s'en apercevoir.
///
/// CE QUE CES TESTS FIXENT VRAIMENT : que le refus intervienne AVANT toute écriture.
/// Un contrôle qui rejetterait après coup laisserait derrière lui une commande, des
/// réservations de stock et une cagnotte engagée à défaire — et la compensation est
/// précisément ce qui se passe mal quand elle est rare.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class TotalAttenduTests
{
    private const decimal LigneTotal = 10_000m;
    private const decimal FraisDeLivraison = 1_500m;
    private const decimal TotalReel = LigneTotal + FraisDeLivraison;

    private static readonly Guid Acheteur = Guid.NewGuid();

    [Fact]
    public async Task Le_paiement_est_refuse_si_le_total_a_change_depuis_l_affichage()
    {
        var depot = new DepotFactice();
        var stock = new StockFactice();

        // L'acheteur avait vu 11 000 — une promotion encore active à l'affichage. Au clic,
        // le panier revalorisé vaut 10 000 + 1 500 de livraison.
        var resultat = await PlacerAsync(depot, stock, totalAttendu: 11_000m);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("ordering.total_changed");

        // Le message doit porter le montant RÉEL : sans lui, l'application n'a rien à
        // réafficher et ne peut que renvoyer l'acheteur au panier.
        resultat.Error.Message.Should().Contain("11500");
    }

    [Fact]
    public async Task Un_refus_n_enregistre_RIEN_et_ne_reserve_RIEN()
    {
        var depot = new DepotFactice();
        var stock = new StockFactice();

        await PlacerAsync(depot, stock, totalAttendu: 9_999m);

        depot.Ajoutees.Should().Be(0, "aucune commande ne doit exister derrière un refus");
        depot.Sauvegardes.Should().Be(0);
        stock.Reservations.Should().Be(0, "réserver puis compenser est le chemin qu'on évite");
    }

    [Fact]
    public async Task Le_paiement_passe_quand_le_total_correspond()
    {
        var depot = new DepotFactice();
        var stock = new StockFactice();

        var resultat = await PlacerAsync(depot, stock, totalAttendu: TotalReel);

        resultat.IsSuccess.Should().BeTrue();
        depot.Ajoutees.Should().Be(1);
    }

    [Fact]
    public async Task Sans_total_attendu_aucun_controle_n_est_fait()
    {
        // Nul veut dire « appelant interne, personne n'a lu de prix ». Le BFF client,
        // lui, refuse l'absence du champ — c'est là que la règle est exigée, parce que
        // c'est la seule surface où un humain a vu un montant.
        var depot = new DepotFactice();
        var stock = new StockFactice();

        var resultat = await PlacerAsync(depot, stock, totalAttendu: null);

        resultat.IsSuccess.Should().BeTrue();
    }

    // ── Montage ─────────────────────────────────────────────────────────────────

    private static async Task<Marketplace.BuildingBlocks.Domain.Results.Result<Guid>> PlacerAsync(
        DepotFactice depot, StockFactice stock, decimal? totalAttendu)
    {
        var handler = new PlaceOrderCommandHandler(
            new PanierFactice(), stock, new SettlementFactice(), new TaxeFactice(), depot, depot);

        return await handler.Handle(
            new PlaceOrderCommand(
                Acheteur,
                new ShippingAddressInput(
                    "Maison", "Awa Kponou", "+2290190000000",
                    "cotonou", "Fidjrossè", "Après le carrefour", null, "BJ", null, null),
                FraisDeLivraison,
                WalletAmount: 0m,
                ExpectedTotal: totalAttendu),
            default);
    }

    /// <summary>
    /// Taxe nulle : ces tests portent sur le TOTAL, et la ventilation ne doit pas
    /// pouvoir le déplacer. Un faux qui renverrait une taxe non nulle sans changer le
    /// résultat des assertions est précisément la démonstration attendue — c'est ce
    /// que vérifie <see cref="TaxeCompriseTests"/>, pas ce fichier.
    /// </summary>
    private sealed class TaxeFactice : ITaxModuleApi
    {
        public Task<TaxResult> ComputeTaxAsync(string jurisdiction, Guid categoryId, decimal amount, CancellationToken cancellationToken = default)
            => Task.FromResult(new TaxResult(amount, 0m, amount, "None", null));

        public Task<IncludedTaxResult> ExtractIncludedTaxAsync(string jurisdiction, Guid categoryId, decimal grossAmount, CancellationToken cancellationToken = default)
            => Task.FromResult(new IncludedTaxResult(grossAmount, grossAmount, 0m, 0m, "None", null));
    }

    private sealed class PanierFactice : ICartModuleApi
    {
        public Task<CartSummary?> GetActiveCartAsync(Guid buyerId, CancellationToken cancellationToken = default)
            => Task.FromResult<CartSummary?>(new CartSummary(
                Guid.NewGuid(), Acheteur, "XOF", "Active",
                new List<CartLineSummary>
                {
                    new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SKU-1",
                        Guid.NewGuid(), 1, LigneTotal, 0m, 0m, LigneTotal, LigneTotal, "XOF"),
                },
                Subtotal: LigneTotal, TotalSellerDiscount: 0m, TotalPlatformDiscount: 0m,
                GrandTotal: LigneTotal));

        public Task<CartSummary?> GetCartAsync(Guid cartId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>Compte les réservations : c'est ce qui prouve qu'un refus n'en fait aucune.</summary>
    private sealed class StockFactice : IInventoryModuleApi
    {
        public int Reservations { get; private set; }

        public Task<bool> TryReserveAsync(
            string sku, Guid locationId, Guid orderId, int quantity, CancellationToken cancellationToken = default)
        {
            Reservations++;
            return Task.FromResult(true);
        }

        public Task<AvailabilitySummary> GetAvailabilityAsync(string sku, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> IsInStockAsync(string sku, int quantity, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ReleaseReservationAsync(
            string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> ConfirmReservationAsync(
            string sku, Guid locationId, Guid orderId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<FulfillmentLocationSummary?> GetLocationAsync(
            Guid locationId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class SettlementFactice : ISettlementModuleApi
    {
        public Task<decimal> GetSpendableCreditAsync(Guid customerId, CancellationToken cancellationToken = default)
            => Task.FromResult(0m);

        public Task<decimal> TryReserveCreditAsync(
            Guid customerId, Guid orderId, decimal amount, string currency, CancellationToken cancellationToken = default)
            => Task.FromResult(0m);

        public Task ConfirmCreditAsync(
            Guid customerId, Guid orderId, decimal amount, string currency, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ReleaseCreditAsync(
            Guid customerId, Guid orderId, decimal amount, string currency, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<decimal> SumDirectRefundsForOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
            => Task.FromResult(0m);
    }

    /// <summary>Dépôt et unité de travail à la fois : ce sont les deux compteurs d'écriture.</summary>
    private sealed class DepotFactice : IOrderRepository, IOrderingUnitOfWork
    {
        public int Ajoutees { get; private set; }
        public int Sauvegardes { get; private set; }

        public Task AddAsync(Order order, CancellationToken cancellationToken = default)
        {
            Ajoutees++;
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Sauvegardes++;
            return Task.FromResult(1);
        }

        public Task<Order?> GetByIdAsync(OrderId id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Order>> ListByBuyerAsync(Guid buyerId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> HasPurchasedAsync(Guid buyerId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Order>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> ListAbandonedIdsAsync(
            DateTime olderThanUtc, int take, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<Order>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<(IReadOnlyList<Order> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
            int page, int pageSize, Guid? id, OrderStatus? status, string? sort, bool desc,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
