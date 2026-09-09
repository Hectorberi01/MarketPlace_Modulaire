using FluentAssertions;
using Marketplace.Modules.Settlement.Domain.Earnings;
using Xunit;

namespace Settlement.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// LE PARTAGE DU BRUT SUIT LA PRÉCISION DE LA DEVISE.
///
/// <c>GrossSplit</c> arrondissait à l'unité EN DUR. C'est juste en franc CFA — la
/// devise de la place — et faux partout ailleurs : en euro, les centimes de la
/// commission et des frais étaient rabotés puis déversés dans le net vendeur, le net
/// étant le reste. Un vendeur touchait ainsi, sans trace, ce que la plateforme aurait
/// dû prélever.
///
/// L'invariant ne bouge pas et reste le seul qui compte :
///     brut = commission + frais provider + net vendeur, exactement, sans reste.
/// Il tient parce que le net est un RESTE et non un troisième arrondi.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class RepartitionSansCentimeTests
{
    [Fact]
    public void En_franc_cfa_les_trois_parts_sont_entieres_et_somment_au_brut()
    {
        var (commission, fraisProvider, net) = GrossSplit.Split(10_007m, 0.10m, 0.05m, "XOF");

        (commission % 1m).Should().Be(0m);
        (fraisProvider % 1m).Should().Be(0m);
        (net % 1m).Should().Be(0m);
        (commission + fraisProvider + net).Should().Be(10_007m);
    }

    [Fact]
    public void En_euro_les_centimes_ne_sont_plus_rabotes_dans_le_net_vendeur()
    {
        var (commission, fraisProvider, net) = GrossSplit.Split(10_007m, 0.10m, 0.05m, "EUR");

        commission.Should().Be(870.17m);
        fraisProvider.Should().Be(435.09m);
        (commission + fraisProvider + net).Should().Be(10_007m);
    }

    [Fact]
    public void La_devise_de_la_place_reste_le_defaut()
        => GrossSplit.Split(10_007m, 0.10m, 0.05m)
            .Should().Be(GrossSplit.Split(10_007m, 0.10m, 0.05m, "XOF"));

    [Fact]
    public void La_contre_passation_reste_entiere_en_franc_cfa()
    {
        var (commission, fraisProvider, _) = GrossSplit.Split(10_007m, 0.10m, 0.05m, "XOF");

        var (c, p, n) = GrossSplit.Prorate(5_000m, 10_007m, commission, fraisProvider, "XOF");

        (c % 1m).Should().Be(0m);
        (p % 1m).Should().Be(0m);
        (n % 1m).Should().Be(0m);
        (c + p + n).Should().Be(5_000m);
    }
}
