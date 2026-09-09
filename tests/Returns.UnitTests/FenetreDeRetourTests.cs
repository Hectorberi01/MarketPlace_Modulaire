using FluentAssertions;
using Marketplace.Modules.Ordering.Contracts;
using Marketplace.Modules.Returns.Application.Abstractions;
using Marketplace.Modules.Returns.Application.Returns;
using Marketplace.Modules.Returns.Domain.Returns;
using Xunit;

namespace Returns.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// LE DROIT AU RETOUR : QUAND IL S'OUVRE, ET QUAND IL SE FERME.
///
/// Ce handler n'acceptait que les commandes `Confirmed`. Or la livraison fait passer
/// la commande à `Delivered` : LE DROIT AU RETOUR S'ÉTEIGNAIT DONC À L'INSTANT PRÉCIS
/// OÙ L'ACHETEUR VOYAIT LA MARCHANDISE — c'est-à-dire au seul moment où il peut
/// constater qu'elle ne convient pas.
///
/// Le défaut est resté invisible aussi longtemps que la boucle de livraison était
/// rompue : aucune commande n'atteignait `Delivered`. Il serait devenu un blocage
/// total le jour même où cette boucle a été réparée. C'est le genre de défaut que
/// deux bugs se masquent mutuellement, et que seule une lecture du parcours entier
/// fait apparaître.
///
/// Symétriquement, il n'existait AUCUNE limite de temps : un retour pouvait être
/// ouvert sur une commande de n'importe quel âge, au prix figé au jour de l'achat.
/// La plateforme portait une créance sans échéance sur chaque ligne jamais retournée.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class FenetreDeRetourTests
{
    private static readonly Guid Acheteur = Guid.NewGuid();
    private static readonly Guid Offre = Guid.NewGuid();
    private static readonly Guid Commande = Guid.NewGuid();

    [Fact]
    public async Task Une_commande_LIVREE_peut_etre_retournee()
    {
        var resultat = await OuvrirAsync(EtatCommande("Delivered", livreeIlYA: TimeSpan.FromDays(2)));

        resultat.IsSuccess.Should().BeTrue(
            "c'est après avoir reçu l'article qu'on découvre qu'il ne convient pas ; "
            + "refuser ici revenait à n'ouvrir le droit au retour que pendant la période "
            + "où l'acheteur n'a rien entre les mains");
    }

    [Fact]
    public async Task Une_commande_CONFIRMEE_mais_pas_encore_livree_reste_retournable()
    {
        // Pas de régression sur l'état d'origine : l'acheteur qui change d'avis pendant
        // que son colis roule ne doit pas attendre de l'avoir reçu.
        var resultat = await OuvrirAsync(EtatCommande("Confirmed"));

        resultat.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Au_dela_de_la_fenetre_le_retour_est_refuse()
    {
        var resultat = await OuvrirAsync(EtatCommande("Delivered", livreeIlYA: TimeSpan.FromDays(8)));

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("returns.window_closed");
    }

    [Fact]
    public async Task Le_dernier_jour_de_la_fenetre_est_encore_ouvert()
    {
        // La borne est INCLUSIVE : sept jours veut dire sept jours pleins, pas six et
        // des heures. Un acheteur qui ouvre son retour le septième jour au soir a
        // respecté le délai qu'on lui a annoncé.
        var resultat = await OuvrirAsync(
            EtatCommande("Delivered", livreeIlYA: TimeSpan.FromDays(7) - TimeSpan.FromMinutes(5)));

        resultat.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Une_commande_livree_SANS_date_de_livraison_reste_retournable()
    {
        // Les commandes livrées avant l'existence de `DeliveredAtUtc`. On ne sait pas
        // quand elles sont arrivées : leur opposer une échéance reviendrait à inventer
        // la date à partir de laquelle on la compte. Le doute profite à l'acheteur.
        var resultat = await OuvrirAsync(EtatCommande("Delivered", livreeIlYA: null));

        resultat.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Une_commande_ni_payee_ni_livree_est_refusee()
    {
        var resultat = await OuvrirAsync(EtatCommande("AwaitingPayment"));

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("returns.order_not_eligible");
    }

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════
    /// UNE LIGNE INTÉGRALEMENT REMBOURSÉE NE SE REMBOURSE PAS UNE SECONDE FOIS.
    ///
    /// `ExistsOpenAsync` interdit un second retour OUVERT — et exclut délibérément
    /// l'état « remboursé ». Une fois le versement fait, la ligne repartait donc avec
    /// son plafond d'origine, autant de fois qu'on ouvrait de retours. L'index
    /// (OrderId, OfferId) n'est pas unique : rien ne bornait le cumul.
    /// ═══════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Fact]
    public async Task Une_ligne_deja_integralement_remboursee_ne_peut_plus_etre_retournee()
    {
        var resultat = await OuvrirAsync(EtatCommande("Confirmed"), dejaRembourseSurLaLigne: 10_000m);

        resultat.IsFailure.Should().BeTrue();
        resultat.Error.Code.Should().Be("returns.line_fully_refunded");
    }

    [Fact]
    public async Task Un_remboursement_partiel_laisse_le_reste_retournable()
    {
        // 10 000 sur la ligne, 4 000 déjà rendus : le nouveau retour part avec 6 000 de
        // plafond, pas 10 000.
        var resultat = await OuvrirAsync(EtatCommande("Confirmed"), dejaRembourseSurLaLigne: 4_000m);

        resultat.IsSuccess.Should().BeTrue();
    }

    // ── Montage ─────────────────────────────────────────────────────────────────

    private static OrderSummary EtatCommande(string statut, TimeSpan? livreeIlYA = null)
        => new(
            Commande, Acheteur, Guid.NewGuid(), "XOF", statut, DateTime.UtcNow.AddDays(-30),
            Subtotal: 10_000m, TotalSellerDiscount: 0m, TotalPlatformDiscount: 0m, GrandTotal: 10_000m,
            Lines: new List<OrderLineSummary>
            {
                new(Offre, Guid.NewGuid(), Guid.NewGuid(), "SKU-1", Guid.NewGuid(), 1,
                    UnitBasePrice: 10_000m, SellerDiscount: 0m, PlatformDiscount: 0m,
                    FinalUnitPrice: 10_000m, LineTotal: 10_000m)
            },
            DeliveredAtUtc: livreeIlYA is { } age ? DateTime.UtcNow - age : null);

    private static async Task<Marketplace.BuildingBlocks.Domain.Results.Result<Guid>> OuvrirAsync(
        OrderSummary order, decimal dejaRembourseSurLaLigne = 0m)
    {
        var handler = new RequestReturnCommandHandler(
            new DepotFactice { DejaRembourseSurLaLigne = dejaRembourseSurLaLigne },
            new OrderingFactice(order), new PolitiqueDeSeptJours(), new UnitOfWorkFactice());

        return await handler.Handle(
            new RequestReturnCommand(Acheteur, Commande, Offre, nameof(ReturnReason.Defective)), default);
    }

    private sealed class PolitiqueDeSeptJours : IReturnPolicySettings
    {
        public TimeSpan WindowAfterDelivery => TimeSpan.FromDays(7);
    }

    private sealed class OrderingFactice : IOrderingModuleApi
    {
        private readonly OrderSummary _order;

        public OrderingFactice(OrderSummary order) => _order = order;

        public Task<OrderSummary?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
            => Task.FromResult<OrderSummary?>(_order);

        public Task<bool> HasPlacedOrderAsync(Guid buyerId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> GetSellerSalesCountAsync(Guid sellerId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Aucun retour ouvert, et l'ajout est accepté sans rien conserver : ces tests
    /// portent sur l'ÉLIGIBILITÉ, pas sur la persistance. Tout le reste lève — un
    /// double factice complaisant ferait passer un test pour la mauvaise raison.
    /// </summary>
    private sealed class DepotFactice : IReturnRequestRepository
    {
        public Task AddAsync(ReturnRequest request, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> ExistsOpenAsync(Guid orderId, Guid offerId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        /// <summary>Ce qui a déjà été remboursé sur la ligne. Zéro sauf test contraire.</summary>
        public decimal DejaRembourseSurLaLigne { get; init; }

        public Task<decimal> SumCommittedRefundsForLineAsync(
            Guid orderId, Guid offerId, CancellationToken cancellationToken = default)
            => Task.FromResult(DejaRembourseSurLaLigne);

        public Task<decimal> SumCommittedRefundsForOrderAsync(
            Guid orderId, CancellationToken cancellationToken = default)
            => Task.FromResult(DejaRembourseSurLaLigne);

        public Task<ReturnRequest?> GetByIdAsync(ReturnRequestId id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ReturnRequest>> ListByBuyerAsync(Guid buyerId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ReturnRequest>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ReturnRequest>> ListRefundedBySellerAsync(
            Guid sellerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ReturnRequest>> ListPendingRefundsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class UnitOfWorkFactice : IReturnsUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
