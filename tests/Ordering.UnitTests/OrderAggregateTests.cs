using FluentAssertions;
using Marketplace.Modules.Ordering.Domain.Orders;
using Xunit;

namespace Ordering.UnitTests;

public class OrderAggregateTests
{
    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LES TROIS DERNIERS MONTANTS SONT DES MONTANTS DE LIGNE, PLUS DES UNITAIRES.
    ///
    /// Cet assistant s'appelait `finalUnit` et passait des remises UNITAIRES. Le
    /// modèle a changé avec la remise de panier : une remise à montant fixe — « −500
    /// sur votre commande » — n'a pas d'équivalent unitaire, et la diviser par la
    /// quantité crée un arrondi par article, donc un écart entre le total affiché et
    /// le total facturé. `OrderLineDraft` porte désormais `SellerDiscount`,
    /// `PlatformDiscount` et `LineTotal` au niveau de la LIGNE.
    ///
    /// Les paramètres sont renommés en conséquence : la signature n'avait pas bougé,
    /// donc rien ne cassait à la compilation — les tests continuaient de passer des
    /// unitaires là où le domaine attendait des totaux, et c'est ce qui les a fait
    /// échouer sans que personne n'ait touché à Ordering.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static OrderLineDraft Line(
        int qty, decimal unitBase, decimal sellerDiscountLine, decimal platformDiscountLine, decimal lineTotal,
        decimal commissionRate = 0.10m, decimal providerFeeRate = 0.05m)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "SKU-1", Guid.NewGuid(), qty, unitBase,
            sellerDiscountLine, platformDiscountLine, lineTotal,
            commissionRate, providerFeeRate);

    private static Order NewOrder(params OrderLineDraft[] lines)
        => Order.Create(Guid.NewGuid(), Guid.NewGuid(), "XOF", lines).Value;

    [Fact]
    public void Create_computes_totals_from_lines()
    {
        // Une ligne de 2 articles à 7000 de base : sous-total 14 000, dont 2800 de
        // remise vendeur et 1000 de remise plateforme, soit 10 200 à payer. Les trois
        // derniers montants sont ceux de la LIGNE, pas d'un article.
        var order = NewOrder(Line(2, 7000m, 2800m, 1000m, 10200m));

        order.Subtotal.Should().Be(14000m);
        order.TotalSellerDiscount.Should().Be(2800m);
        order.TotalPlatformDiscount.Should().Be(1000m);
        order.GrandTotal.Should().Be(10200m);
        // Le prix unitaire n'est plus qu'un affichage déduit du montant de ligne.
        order.Lines.Single().FinalUnitPrice.Should().Be(5100m);
        order.Status.Should().Be(OrderStatus.Pending);
    }

    [Fact]
    public void MarkDelivered_depuis_confirmed_passe_en_livre()
    {
        var order = NewOrder(Line(1, 7000m, 0m, 0m, 7000m));
        order.MarkAwaitingPayment();
        order.MarkPaid();
        order.Confirm();

        order.MarkDelivered().IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Delivered);
    }

    [Fact]
    public void MarkDelivered_refuse_si_pas_confirmee()
    {
        var order = NewOrder(Line(1, 7000m, 0m, 0m, 7000m));
        order.MarkAwaitingPayment();

        order.MarkDelivered().IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Create_with_no_lines_fails()
    {
        var result = Order.Create(Guid.NewGuid(), Guid.NewGuid(), "XOF", Array.Empty<OrderLineDraft>());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ordering.no_lines");
    }

    [Fact]
    public void Happy_path_transitions_to_confirmed()
    {
        var order = NewOrder(Line(1, 7000m, 0m, 0m, 7000m));

        order.MarkAwaitingPayment().IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.AwaitingPayment);

        order.MarkPaid().IsSuccess.Should().BeTrue();
        order.Confirm().IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public void Confirm_before_payment_is_rejected()
    {
        var order = NewOrder(Line(1, 7000m, 0m, 0m, 7000m));
        order.MarkAwaitingPayment();

        order.Confirm().IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Confirmed_order_cannot_be_cancelled()
    {
        var order = NewOrder(Line(1, 7000m, 0m, 0m, 7000m));
        order.MarkAwaitingPayment();
        order.MarkPaid();
        order.Confirm();

        order.Cancel("test").IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Place_then_cancel_releases_state()
    {
        var order = NewOrder(Line(1, 7000m, 0m, 0m, 7000m));
        order.MarkAwaitingPayment();

        order.Cancel("changed mind").IsSuccess.Should().BeTrue();
        order.Status.Should().Be(OrderStatus.Cancelled);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // CAGNOTTE : la commande distingue ce qu'elle VAUT de ce qu'il reste À ENCAISSER.
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void La_cagnotte_reduit_le_montant_du_sans_toucher_a_la_valeur_de_la_commande()
    {
        var order = NewOrder(Line(2, 7000m, 2800m, 1000m, 10200m));
        order.SetShippingFee(1500m);

        order.ApplyWalletCredit(4000m).IsSuccess.Should().BeTrue();

        // Le vendeur est payé sur la valeur, la commission se calcule sur la valeur :
        // GrandTotal ne doit pas bouger d'un franc.
        order.GrandTotal.Should().Be(11700m);
        order.WalletApplied.Should().Be(4000m);
        order.AmountDue.Should().Be(7700m);
        order.IsFullyCoveredByWallet.Should().BeFalse();
    }

    [Fact]
    public void Une_cagnotte_superieure_au_total_est_plafonnee_et_ne_cree_pas_de_monnaie()
    {
        var order = NewOrder(Line(1, 5000m, 0m, 0m, 5000m));

        order.ApplyWalletCredit(9_999m).IsSuccess.Should().BeTrue();

        order.WalletApplied.Should().Be(5000m);
        order.AmountDue.Should().Be(0m);
        order.IsFullyCoveredByWallet.Should().BeTrue();
    }

    [Fact]
    public void La_cagnotte_ne_s_applique_plus_une_fois_la_commande_engagee_au_paiement()
    {
        var order = NewOrder(Line(1, 5000m, 0m, 0m, 5000m));
        order.MarkAwaitingPayment();

        // LE MONTANT ANNONCÉ AU PRESTATAIRE NE BOUGE PLUS. Sans ce garde-fou, un second
        // appel pourrait réduire le dû après qu'un paiement a été initié pour le montant
        // plein — le client serait débité de la totalité ET verrait sa cagnotte entamée.
        var applied = order.ApplyWalletCredit(1000m);

        applied.IsFailure.Should().BeTrue();
        applied.Error.Code.Should().Be("ordering.wallet_credit_too_late");
        order.WalletApplied.Should().Be(0m);
        order.AmountDue.Should().Be(5000m);
    }

    [Fact]
    public void Sans_cagnotte_le_montant_du_reste_le_total_de_la_commande()
    {
        var order = NewOrder(Line(1, 5000m, 0m, 0m, 5000m));
        order.SetShippingFee(1500m);

        order.WalletApplied.Should().Be(0m);
        order.AmountDue.Should().Be(order.GrandTotal);
        order.IsFullyCoveredByWallet.Should().BeFalse("une commande sans cagnotte n'est jamais « couverte »");
    }
}
