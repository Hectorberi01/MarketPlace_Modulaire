using FluentAssertions;
using Marketplace.BuildingBlocks.Domain.Geography;
using Marketplace.Modules.Shipping.Domain.Zones;
using Xunit;

namespace Shipping.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// LE RÉSOLVEUR DÉCIDE COMBIEN L'ACHETEUR PAIE. C'EST DONC LUI QU'ON TESTE.
///
/// Tout le reste de la chaîne — table, écran d'administration, endpoint — n'est que
/// de la plomberie autour de cette fonction. Une erreur ici ne se voit pas : elle
/// facture simplement le mauvais montant, indéfiniment, à des gens qui n'ont aucun
/// moyen de savoir que le tarif est faux.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public class ShippingZoneResolverTests
{
    // Deux fabriques plutôt qu'une seule avec un `bool fallback = false` placé avant le
    // `params` : en C#, un argument nommé ne peut pas être suivi d'arguments
    // positionnels (CS8323). La forme `Zone("sud", communes: "a", "b")` ne compilait
    // donc pas. Séparer lève l'ambiguïté, et se lit mieux — une zone de repli n'a de
    // toute façon jamais de commune rattachée.
    private static ShippingZone Zone(string code, params string[] communes)
    {
        var zone = ShippingZone.Create(code, code, false).Value;
        zone.SetCommunes(communes).IsSuccess.Should().BeTrue();
        return zone;
    }

    private static ShippingZone FallbackZone(string code) => ShippingZone.Create(code, code, true).Value;

    [Fact]
    public void Une_commune_rattachee_resout_vers_sa_zone()
    {
        var cotonou = Zone("cotonou", "cotonou");
        var national = FallbackZone("national");

        ShippingZoneResolver.Resolve([cotonou, national], "cotonou").Should().Be(cotonou);
    }

    [Fact]
    public void Une_commune_non_rattachee_tombe_sur_le_repli()
    {
        var cotonou = Zone("cotonou", "cotonou");
        var national = FallbackZone("national");

        ShippingZoneResolver.Resolve([cotonou, national], "natitingou").Should().Be(national);
    }

    [Fact]
    public void Sans_repli_actif_aucune_zone_nest_renvoyee()
    {
        // Le cas que l'appelant DOIT traiter : mieux vaut un refus explicite qu'un
        // tarif inventé.
        var cotonou = Zone("cotonou", "cotonou");

        ShippingZoneResolver.Resolve([cotonou], "natitingou").Should().BeNull();
    }

    [Fact]
    public void Une_zone_inactive_est_ignoree_et_le_repli_prend_la_main()
    {
        var cotonou = Zone("cotonou", "cotonou");
        var national = FallbackZone("national");
        cotonou.Deactivate().IsSuccess.Should().BeTrue();

        ShippingZoneResolver.Resolve([cotonou, national], "cotonou").Should().Be(national);
    }

    [Fact]
    public void Deux_zones_couvrant_la_meme_commune_la_plus_specifique_gagne()
    {
        // Rien en base n'empêche deux administrateurs de cocher Cotonou deux fois.
        // Sans règle stable, le tarif dépendrait de l'ordre de lecture — donc varierait
        // d'une requête à l'autre, sans que personne ne comprenne pourquoi.
        var large = Zone("sud", "cotonou", "abomey-calavi", "porto-novo");
        var precise = Zone("cotonou", "cotonou");

        ShippingZoneResolver.Resolve([large, precise], "cotonou").Should().Be(precise);
        ShippingZoneResolver.Resolve([precise, large], "cotonou").Should().Be(precise);
    }

    [Fact]
    public void A_specificite_egale_la_resolution_reste_deterministe()
    {
        var a = Zone("alpha", "cotonou");
        var b = Zone("beta", "cotonou");

        var first = ShippingZoneResolver.Resolve([a, b], "cotonou");
        var second = ShippingZoneResolver.Resolve([b, a], "cotonou");

        first.Should().Be(second, "l'ordre de lecture ne doit jamais changer le tarif facturé");
    }

    [Fact]
    public void Le_repli_ne_peut_pas_etre_desactive()
    {
        // C'est lui qui garantit qu'aucune commande ne reste sans tarif.
        var national = FallbackZone("national");

        var result = national.Deactivate();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("shipping.zone.fallback_required");
        national.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Une_commune_inconnue_est_refusee_au_rattachement()
    {
        var zone = ShippingZone.Create("test", "Test", false).Value;

        var result = zone.SetCommunes(["cotonou", "lome"]);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("shipping.zone.commune_unknown");
        zone.CommuneCodes.Should().BeEmpty("un rattachement partiel serait pire qu'un refus");
    }

    [Fact]
    public void Le_rattachement_accepte_un_libelle_et_le_normalise_en_code()
    {
        var zone = ShippingZone.Create("test", "Test", false).Value;

        zone.SetCommunes(["Abomey-Calavi", "COTONOU", "Sèmè-Podji"]).IsSuccess.Should().BeTrue();

        zone.CommuneCodes.Should().BeEquivalentTo(["abomey-calavi", "cotonou", "seme-podji"]);
    }

    [Fact]
    public void SetCommunes_remplace_au_lieu_de_fusionner()
    {
        // L'écran d'administration présente des cases à cocher : décocher doit détacher.
        var zone = Zone("test", "cotonou", "parakou");

        zone.SetCommunes(["cotonou"]).IsSuccess.Should().BeTrue();

        zone.CommuneCodes.Should().Equal("cotonou");
    }

    [Fact]
    public void Les_77_communes_trouvent_toujours_une_zone_grace_au_repli()
    {
        // Le test qui compte vraiment : AUCUN acheteur ne doit se voir refuser sa
        // commande faute de tarif, où qu'il habite.
        var cotonou = Zone("cotonou", "cotonou");
        var national = FallbackZone("national");
        var zones = new[] { cotonou, national };

        foreach (var commune in BeninGeography.Communes)
        {
            ShippingZoneResolver.Resolve(zones, commune.Code)
                .Should().NotBeNull($"la commune « {commune.Name} » doit être livrable");
        }
    }

    [Fact]
    public void Les_communes_non_couvertes_sont_recensees_pour_ladministrateur()
    {
        var cotonou = Zone("cotonou", "cotonou");
        var national = FallbackZone("national");

        var uncovered = ShippingZoneResolver.UncoveredCommunes(
            [cotonou, national],
            BeninGeography.Communes.Select(c => c.Code));

        // Le repli ne « couvre » pas au sens de l'écran : il rattrape. Les 76 autres
        // communes doivent apparaître comme à rattacher, sans quoi elles resteraient
        // au tarif le plus large sans que personne ne le remarque.
        uncovered.Should().HaveCount(BeninGeography.Communes.Count - 1);
        uncovered.Should().NotContain("cotonou");
    }

    [Fact]
    public void Un_tarif_negatif_est_refuse()
    {
        var zone = ShippingZone.Create("test", "Test", false).Value;

        zone.SetRate("standard", "Standard", "24h", -1m, "XOF").IsFailure.Should().BeTrue();
    }

    [Fact]
    public void SetRate_met_a_jour_le_service_existant_au_lieu_den_ajouter_un_second()
    {
        var zone = ShippingZone.Create("test", "Test", false).Value;

        zone.SetRate("standard", "Standard", "24h", 1500m, "XOF").IsSuccess.Should().BeTrue();
        zone.SetRate("standard", "Standard", "24h à 48h", 2000m, "XOF").IsSuccess.Should().BeTrue();

        zone.Rates.Should().ContainSingle();
        zone.Rates[0].Amount.Should().Be(2000m);
    }
}
